using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Delta.Shader.Analyzers;
using Final = Delta.Shader.Contract;

namespace Delta.Shader.Tool;

internal static class MathsConformancePublisher
{
    private static readonly HashSet<string> OperationNames = new(StringComparer.Ordinal)
    {
        "abs",
        "min",
        "max",
        "clamp",
        "sqrt",
        "sin",
        "dot",
        "cross",
        "length",
        "normalize",
        "Abs",
        "Min",
        "Max",
        "Clamp",
        "Sqrt",
        "Sin",
        "Dot",
        "Cross",
        "Length",
        "Normalize"
    };

    private static readonly HashSet<string> FloatSliceTypes = new(StringComparer.Ordinal)
    {
        "float",
        "float2",
        "float3",
        "float4"
    };


    public static async Task<int> PublishAsync(
        string mathsRoot,
        string outputDirectory,
        ShaderCompilationOptions options)
    {
        if (string.IsNullOrWhiteSpace(mathsRoot) || !Directory.Exists(mathsRoot))
        {
            await Console.Error.WriteLineAsync($"Maths conformance failed: DeltaMaths root does not exist: {mathsRoot}").ConfigureAwait(false);
            return 1;
        }

        var manifestPath = Path.Combine(
            mathsRoot,
            "src",
            "DeltaMaths",
            "Vectors",
            "shader-contract.json");
        var bundlePath = Path.Combine(
            mathsRoot,
            "tests",
            "DeltaMaths.Conformance",
            "shader-conformance.json");
        var mathsAssemblyPath = Path.Combine(
            mathsRoot,
            "src",
            "DeltaMaths",
            "bin",
            "Release",
            "net10.0",
            "DeltaMaths.dll");
        if (!File.Exists(manifestPath) || !File.Exists(bundlePath))
        {
            await Console.Error.WriteLineAsync(
                $"Maths conformance failed: expected handoff files were not found under {mathsRoot}. "
                + "Build the DeltaMaths conformance bundle first.").ConfigureAwait(false);
            return 1;
        }

        if (!File.Exists(mathsAssemblyPath))
        {
            await Console.Error.WriteLineAsync(
                $"Maths conformance failed: missing {mathsAssemblyPath}. "
                + "Build DeltaMaths -c Release before publishing artifacts.").ConfigureAwait(false);
            return 1;
        }

        var glslang = FindTool("glslangValidator");
        var spirvValidator = FindTool("spirv-val");
        var spirvOptimizer = options.Optimization == ShaderOptimizationMode.None
            ? null
            : FindTool("spirv-opt");
        if (glslang is null || spirvValidator is null ||
            (options.Optimization != ShaderOptimizationMode.None && spirvOptimizer is null))
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: glslangValidator, spirv-opt and spirv-val must be installed in PATH.").ConfigureAwait(false);
            return 1;
        }

        var manifestFunctions = MathsConformanceBundleReader.LoadFunctions(manifestPath);
        ConformanceBundle bundle;
        try
        {
            bundle = MathsConformanceBundleReader.LoadCaseBundle(bundlePath);
        }
        catch (InvalidOperationException exception)
        {
            await Console.Error.WriteLineAsync($"Maths conformance failed: invalid bundle: {exception.Message}").ConfigureAwait(false);
            return 1;
        }

        var cases = bundle.Cases;
        var duplicateCaseIdentities = cases
            .GroupBy(conformanceCase => conformanceCase.Operation.Identity, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateCaseIdentities.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: duplicate bundle operation identities: "
                + string.Join(", ", duplicateCaseIdentities)).ConfigureAwait(false);
            return 1;
        }

        var casesByIdentity = cases.ToDictionary(
            conformanceCase => conformanceCase.Operation.Identity,
            StringComparer.Ordinal);

        var duplicateManifestIdentities = manifestFunctions
            .GroupBy(function => function.Identity, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateManifestIdentities.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: duplicate manifest identities: "
                + string.Join(", ", duplicateManifestIdentities)).ConfigureAwait(false);
            return 1;
        }

        var manifestIdentities = manifestFunctions
            .Select(function => function.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var missingManifestFunctions = cases
            .Select(conformanceCase => conformanceCase.Operation.Identity)
            .Where(identity => !manifestIdentities.Contains(identity))
            .ToArray();
        if (missingManifestFunctions.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: bundle identities are absent from the contract manifest:\n"
                + string.Join(Environment.NewLine, missingManifestFunctions)).ConfigureAwait(false);
            return 1;
        }

        var mappingMismatches = cases
            .Where(conformanceCase => manifestFunctions.Single(function => function.Identity == conformanceCase.Operation.Identity).Mapping != conformanceCase.Operation.Mapping)
            .Select(conformanceCase => conformanceCase.Operation.Identity)
            .ToArray();
        if (mappingMismatches.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: bundle/manifest mapping mismatch for:\n"
                + string.Join(Environment.NewLine, mappingMismatches)).ConfigureAwait(false);
            return 1;
        }

        var firstSliceFunctions = manifestFunctions
            .Where(IsFirstSliceFunction)
            .OrderBy(function => function.Identity, StringComparer.Ordinal);
        var remainingFunctions = manifestFunctions
            .Where(function => !IsFirstSliceFunction(function))
            .OrderBy(function => function.Identity, StringComparer.Ordinal);
        var functions = firstSliceFunctions
            .Concat(remainingFunctions)
            .Where(function => casesByIdentity.ContainsKey(function.Identity))
            .ToArray();
        var missingCases = functions
            .Select(function => function.Identity)
            .Where(identity => !casesByIdentity.ContainsKey(identity))
            .ToArray();
        if (missingCases.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: selected manifest identities are absent from the case bundle:\n"
                + string.Join(Environment.NewLine, missingCases)).ConfigureAwait(false);
            return 1;
        }

        if (functions.Length == 0)
        {
            await Console.Error.WriteLineAsync("Maths conformance failed: the supported case selection is empty.").ConfigureAwait(false);
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);
        var casesDirectory = Path.Combine(outputDirectory, "cases");
        Directory.CreateDirectory(casesDirectory);

        var references = CreateReferences(mathsAssemblyPath, typeof(ComputeShaderAttribute).Assembly.Location);
        var entriesByIndex = new PublishedCase[functions.Length];
        var compileCaseIndices = new int[functions.Length];
        var compileFunctions = new ContractFunction[functions.Length];
        var compileCount = 0;
        for (var caseIndex = 0; caseIndex < functions.Length; caseIndex++)
        {
            var function = functions[caseIndex];
            var caseData = casesByIdentity[function.Identity];
            var caseId = $"maths-{caseIndex:0000}";
            var entry = PublishedCase.FromCase(caseData, caseId);
            entriesByIndex[caseIndex] = entry;
            compileCaseIndices[compileCount] = caseIndex;
            compileFunctions[compileCount] = function;
            compileCount++;
        }

        Array.Resize(ref compileCaseIndices, compileCount);
        Array.Resize(ref compileFunctions, compileCount);
        var fixtureSource = MathsConformanceFixtureBuilder.Build(compileFunctions);
        var fixtureSourcePath = Path.Combine(outputDirectory, "MathsConformanceFixtures.cs");
        await File.WriteAllTextAsync(fixtureSourcePath, fixtureSource, Encoding.UTF8).ConfigureAwait(false);

        var resultsByCaseIndex = new ShaderCompilationResult?[functions.Length];
        var glslByCaseIndex = new string?[functions.Length];
        var readyByCaseIndex = new bool[functions.Length];
        if (compileCount != 0)
        {
            const string compilationSeed = """
                namespace Delta.Shader.MathsConformance.Generated;

                internal static class MathsConformanceBatchSeed
                {
                }
                """;
            var compilation = CreateCompilation(
                compilationSeed,
                references,
                "DeltaShaderMathsConformanceBatch");
            GeneratorDriver generatorDriver = CSharpGeneratorDriver.Create(
                new[] { new MathsConformanceFixtureGenerator(fixtureSource).AsSourceGenerator() },
                parseOptions: compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions);
            generatorDriver = generatorDriver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var generatedCompilation,
                out var generatorDiagnostics);
            var generatorErrors = generatorDiagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToArray();
            var compilationResults = ShaderCompiler.CompileAll(generatedCompilation, options);
            var sourceIdentityToCaseIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var fixtureType = generatedCompilation.GetTypeByMetadataName(
                "Delta.Shader.MathsConformance.Generated.MathsConformanceFixtures");
            if (fixtureType is not null)
            {
                foreach (var method in fixtureType.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!method.Name.StartsWith("Case", StringComparison.Ordinal)
                        || !int.TryParse(
                            method.Name.AsSpan(4),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var fixtureIndex)
                        || fixtureIndex < 0
                        || fixtureIndex >= compileCount)
                    {
                        continue;
                    }

                    sourceIdentityToCaseIndex[GetFullSymbolIdentity(method)] =
                        compileCaseIndices[fixtureIndex];
                }
            }

            foreach (var result in compilationResults)
            {
                if (sourceIdentityToCaseIndex.TryGetValue(result.SourceMethodIdentity, out var caseIndex))
                {
                    resultsByCaseIndex[caseIndex] = result;
                }
            }

            for (var compileIndex = 0; compileIndex < compileCount; compileIndex++)
            {
                var caseIndex = compileCaseIndices[compileIndex];
                if (entriesByIndex[caseIndex] is not { } entry)
                {
                    continue;
                }

                var result = resultsByCaseIndex[caseIndex];
                if (result is null)
                {
                    entry.Status = "compiler-diagnostic";
                    entry.Diagnostic = generatorErrors.Length == 0
                        ? "No result matched the full source symbol identity."
                        : string.Join(Environment.NewLine, generatorErrors);
                    continue;
                }

                if (!result.Success || result.Module is null || result.BuildManifest is null)
                {
                    var diagnostic = string.Join(
                        Environment.NewLine,
                        result.Diagnostics.Select(item => $"{item.Id}: {item.Message}"));
                    entry.Status = "compiler-diagnostic";
                    entry.Diagnostic = diagnostic.Length == 0
                        ? "Shader compilation failed without a diagnostic."
                        : diagnostic;
                    continue;
                }

                var emit = GlslEmitter.EmitFromModule(result.Module);
                if (!emit.Success)
                {
                    entry.Status = "glsl-diagnostic";
                    entry.Diagnostic = "GLSL emission failed.";
                    continue;
                }

                glslByCaseIndex[caseIndex] = AddRequiredExtensions(
                    emit.Source,
                    entriesByIndex[caseIndex].RequiredCapabilities);
                readyByCaseIndex[caseIndex] = true;
            }
        }

        static string GetFullSymbolIdentity(IMethodSymbol method)
        {
            var parameters = string.Join(
                ",",
                method.Parameters.Select(parameter =>
                    parameter.RefKind + ":" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "." + method.Name + "`" + method.Arity + "(" + parameters + ")";
        }

        var workOrder = new int[functions.Length];
        for (var index = 0; index < workOrder.Length; index++)
        {
            workOrder[index] = index;
        }

        Array.Sort(workOrder, (left, right) =>
        {
            var leftFunction = functions[left];
            var rightFunction = functions[right];
            var comparison = StringComparer.Ordinal.Compare(leftFunction.ReturnType, rightFunction.ReturnType);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = leftFunction.ParameterTypes.Length.CompareTo(rightFunction.ParameterTypes.Length);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(leftFunction.Identity, rightFunction.Identity);
        });
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };
        Parallel.For(0, workOrder.Length, parallelOptions, workIndex =>
        {
            var caseIndex = workOrder[workIndex];
            if (!readyByCaseIndex[caseIndex]
                || resultsByCaseIndex[caseIndex] is not { BuildManifest: not null } result
                || glslByCaseIndex[caseIndex] is not { } glsl)
            {
                return;
            }

            var caseId = $"maths-{caseIndex:0000}";
            var stem = $"{caseId}.comp";
            var glslPath = Path.Combine(casesDirectory, $"{stem}.glsl");
            var buildManifestPath = Path.Combine(casesDirectory, $"{stem}.shader.json");
            File.WriteAllText(glslPath, glsl, Encoding.UTF8);
            File.WriteAllText(
                buildManifestPath,
                JsonSerializer.Serialize(result.BuildManifest, JsonOptions),
                Encoding.UTF8);
        });

        Parallel.For(0, workOrder.Length, parallelOptions, workIndex =>
        {
            var caseIndex = workOrder[workIndex];
            if (!readyByCaseIndex[caseIndex]
                || resultsByCaseIndex[caseIndex] is not { BuildManifest: not null } result
                || entriesByIndex[caseIndex] is not { } entry)
            {
                return;
            }

            var caseId = $"maths-{caseIndex:0000}";
            var stem = $"{caseId}.comp";
            var glslPath = Path.Combine(casesDirectory, $"{stem}.glsl");
            var spirvPath = Path.Combine(casesDirectory, $"{stem}.spv");
            var abiPath = Path.Combine(casesDirectory, $"{stem}.abi.json");
            var optimizationFlag = options.Optimization switch
            {
                ShaderOptimizationMode.Performance => "-O",
                ShaderOptimizationMode.Size => "-Os",
                _ => null
            };
            var compile = optimizationFlag is null
                ? ProcessRunner.Run(glslang, "-V", "--target-env", options.Profile, "-S", "comp", glslPath, "-o", spirvPath)
                : ProcessRunner.Run(glslang, "-V", "--target-env", options.Profile, optimizationFlag, "-S", "comp", glslPath, "-o", spirvPath);
            if (compile.ExitCode != 0)
            {
                entry.Status = "glslang-diagnostic";
                entry.Diagnostic = compile.Output;
                entry.ArtifactPath = Path.GetRelativePath(outputDirectory, glslPath);
                return;
            }

            var optimization = SpirvOptimizer.Run(
                spirvOptimizer,
                options.Profile,
                options.Optimization,
                spirvPath);
            if (optimization.ExitCode != 0)
            {
                entry.Status = "spirv-optimization-diagnostic";
                entry.Diagnostic = optimization.Output;
                entry.ArtifactPath = Path.GetRelativePath(outputDirectory, spirvPath);
                return;
            }

            var validation = ProcessRunner.Run(spirvValidator, "--target-env", options.Profile, spirvPath);
            if (validation.ExitCode != 0)
            {
                entry.Status = "spirv-validation-diagnostic";
                entry.Diagnostic = validation.Output;
                entry.ArtifactPath = Path.GetRelativePath(outputDirectory, spirvPath);
                return;
            }

            var artifact = ShaderArtifactPublisher.Create(
                File.ReadAllBytes(spirvPath),
                result.BuildManifest,
                ResolveCapabilities(entry.RequiredCapabilities));
            var resolvedAbi = new ResolvedAbiDocument
            {
                CaseId = caseId,
                OperationIdentity = functions[caseIndex].Identity,
                EntryPointName = artifact.EntryPoint,
                Stage = artifact.Stage,
                ArtifactPath = Path.GetRelativePath(outputDirectory, spirvPath),
                Abi = ResolvedShaderAbi.From(artifact.Abi)
            };
            File.WriteAllText(
                abiPath,
                JsonSerializer.Serialize(resolvedAbi, JsonOptions),
                Encoding.UTF8);

            entry.Status = "passed";
            entry.ArtifactPath = Path.GetRelativePath(outputDirectory, spirvPath);
            entry.AbiPath = Path.GetRelativePath(outputDirectory, abiPath);
        });

        var entries = entriesByIndex.ToList();

        var conformanceIndex = new ConformanceIndex
        {
            ContractPath = Path.GetRelativePath(outputDirectory, manifestPath),
            BundlePath = Path.GetRelativePath(outputDirectory, bundlePath),
            FixtureSourcePath = Path.GetRelativePath(outputDirectory, fixtureSourcePath),
            SelectedCount = entries.Count,
            BundleCaseCount = cases.Length,
            ManifestFunctionCount = bundle.Coverage.ManifestFunctionCount,
            SupportedCaseCount = bundle.Coverage.SupportedCount,
            UnsupportedManifestCount = bundle.Coverage.UnsupportedManifestCount,
            ExcludedCaseCount = bundle.Coverage.ExcludedCount,
            ArtifactCount = entries.Count(entry => entry.Status == "passed"),
            CompilerBlockedCount = entries.Count(IsCompilerBlocked),
            CapabilityBlockedCount = entries.Count(entry => entry.Status == "capability-blocked"),
            BackendBlockedCount = entries.Count(entry => entry.Status == "glsl-diagnostic"),
            ExternalValidationBlockedCount = entries.Count(entry => entry.Status is "glslang-diagnostic" or "spirv-optimization-diagnostic" or "spirv-validation-diagnostic"),
            MismatchedCount = 0,
            AccountedCount = entries.Count(entry => entry.Status.Length != 0),
            Cases = entries
        };
        var indexPath = Path.Combine(outputDirectory, "index.json");
        await File.WriteAllTextAsync(
                indexPath,
                JsonSerializer.Serialize(conformanceIndex, JsonOptions),
                Encoding.UTF8)
            .ConfigureAwait(false);

        await Console.Out.WriteLineAsync(
            $"Maths conformance artifacts: {conformanceIndex.ArtifactCount}/{conformanceIndex.SelectedCount} passed; "
            + $"blocked compiler={conformanceIndex.CompilerBlockedCount}, backend={conformanceIndex.BackendBlockedCount}, "
            + $"external={conformanceIndex.ExternalValidationBlockedCount}; index: {indexPath}").ConfigureAwait(false);
        foreach (var entry in entries.Where(entry => entry.Status != "passed"))
        {
            await Console.Out.WriteLineAsync($"[BLOCKED] {entry.OperationIdentity}: {entry.Status}: {entry.Diagnostic}").ConfigureAwait(false);
        }

        return conformanceIndex.ArtifactCount == conformanceIndex.SelectedCount ? 0 : 1;
    }

    private static bool IsCompilerBlocked(PublishedCase entry)
        => entry.Status is "roslyn-diagnostic" or "compiler-diagnostic";

    private static Final.ShaderCapabilities ResolveCapabilities(
        IReadOnlyList<string> requiredCapabilities)
    {
        var capabilities = Final.ShaderCapabilities.None;
        foreach (var requiredCapability in requiredCapabilities)
        {
            capabilities |= requiredCapability switch
            {
                "float16" => Final.ShaderCapabilities.HalfPrecisionFloatingPoint,
                "float64" => Final.ShaderCapabilities.DoublePrecisionFloatingPoint,
                _ => Final.ShaderCapabilities.None
            };
        }

        return capabilities;
    }

    private static string AddRequiredExtensions(
        string source,
        IReadOnlyList<string> requiredCapabilities)
    {
        var extensions = new List<string>();
        foreach (var requiredCapability in requiredCapabilities)
        {
            var extension = requiredCapability switch
            {
                "float16" => "#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require",
                "float64" => "#extension GL_ARB_gpu_shader_fp64 : require",
                _ => null
            };

            if (extension is not null && !extensions.Contains(extension, StringComparer.Ordinal))
            {
                extensions.Add(extension);
            }
        }

        if (extensions.Count == 0)
        {
            return source;
        }

        var firstNewLine = source.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewLine < 0)
        {
            return source + Environment.NewLine + string.Join(Environment.NewLine, extensions);
        }

        return string.Concat(
            source.AsSpan(0, firstNewLine + 1),
            string.Join(Environment.NewLine, extensions),
            Environment.NewLine,
            source.AsSpan(firstNewLine + 1));
    }

    private static bool IsFirstSliceFunction(ContractFunction function)
        => OperationNames.Contains(function.MethodName)
            && IsFloatSliceType(function.OwnerType)
            && FloatSliceTypes.Contains(function.ReturnType)
            && function.ParameterTypes.All(FloatSliceTypes.Contains);

    private static bool IsFloatSliceType(string typeName)
        => typeName == "maths" || FloatSliceTypes.Contains(typeName);

    private static PortableExecutableReference[] CreateReferences(params string[] requiredAssemblies)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies is not null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                paths.Add(path);
            }
        }

        foreach (var assembly in requiredAssemblies)
        {
            if (string.IsNullOrWhiteSpace(assembly) || !File.Exists(assembly))
            {
                throw new FileNotFoundException($"Required compilation reference was not found: {assembly}");
            }

            paths.Add(assembly);
        }

        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        IReadOnlyList<MetadataReference> references,
        string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string? FindTool(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

}
