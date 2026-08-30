using System.Diagnostics;
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
    private static readonly HashSet<string> CompilerBlockedCapabilities = new(StringComparer.Ordinal)
    {
        "float16",
        "float64"
    };

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
        if (glslang is null || spirvValidator is null)
        {
            await Console.Error.WriteLineAsync(
                "Maths conformance failed: both glslangValidator and spirv-val must be installed in PATH.").ConfigureAwait(false);
            return 1;
        }

        var manifestFunctions = LoadFunctions(manifestPath);
        ConformanceBundle bundle;
        try
        {
            bundle = LoadCaseBundle(bundlePath);
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
            if (TryGetCompilerBlockedCapability(caseData, out var blockedCapability))
            {
                entry.Status = "capability-blocked";
                entry.Diagnostic =
                    $"DeltaShader base profile does not lower the required '{blockedCapability}' capability.";
                entriesByIndex[caseIndex] = entry;
                continue;
            }

            entriesByIndex[caseIndex] = entry;
            compileCaseIndices[compileCount] = caseIndex;
            compileFunctions[compileCount] = function;
            compileCount++;
        }

        Array.Resize(ref compileCaseIndices, compileCount);
        Array.Resize(ref compileFunctions, compileCount);
        var fixtureSource = BuildFixtureSource(compileFunctions);
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

                glslByCaseIndex[caseIndex] = emit.Source;
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
            var compile = RunTool(
                glslang,
                "-V",
                "--target-env",
                options.Profile,
                "-S",
                "comp",
                glslPath,
                "-o",
                spirvPath);
            if (compile.ExitCode != 0)
            {
                entry.Status = "glslang-diagnostic";
                entry.Diagnostic = compile.Output;
                entry.ArtifactPath = Path.GetRelativePath(outputDirectory, glslPath);
                return;
            }

            var validation = RunTool(spirvValidator, "--target-env", options.Profile, spirvPath);
            if (validation.ExitCode != 0)
            {
                entry.Status = "spirv-validation-diagnostic";
                entry.Diagnostic = validation.Output;
                entry.ArtifactPath = Path.GetRelativePath(outputDirectory, spirvPath);
                return;
            }

            var artifact = ShaderArtifactPublisher.Create(File.ReadAllBytes(spirvPath), result.BuildManifest);
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
            ExternalValidationBlockedCount = entries.Count(entry => entry.Status is "glslang-diagnostic" or "spirv-validation-diagnostic"),
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

    private static bool TryGetCompilerBlockedCapability(
        ConformanceCase conformanceCase,
        out string capability)
    {
        capability = conformanceCase.RequiredCapabilities
            .FirstOrDefault(CompilerBlockedCapabilities.Contains)
            ?? string.Empty;
        return capability.Length != 0;
    }

    private static List<ContractFunction> LoadFunctions(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var functions = new List<ContractFunction>();
        foreach (var element in document.RootElement.GetProperty("functions").EnumerateArray())
        {
            var mapping = element.GetProperty("mapping").GetString();
            if (mapping is not ("Builtin" or "Helper"))
            {
                continue;
            }

            var parameters = element.GetProperty("parameterClrNames")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();
            var modifiers = element.TryGetProperty("parameterModifiers", out var modifierElement)
                ? ReadStrings(modifierElement)
                : parameters.Select(_ => "none").ToArray();
            functions.Add(new ContractFunction(
                element.GetProperty("identity").GetString() ?? string.Empty,
                element.GetProperty("typeClrName").GetString() ?? string.Empty,
                element.GetProperty("clrName").GetString() ?? string.Empty,
                parameters,
                modifiers,
                element.GetProperty("returnClrName").GetString() ?? string.Empty,
                element.GetProperty("mapping").GetString() ?? string.Empty,
                element.GetProperty("glslName").GetString() ?? string.Empty));
        }

        return functions;
    }

    private static ConformanceBundle LoadCaseBundle(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidOperationException("schemaVersion must be 1.");
        }

        if (!string.Equals(
                root.GetProperty("protocol").GetString(),
                "math-cpu-gpu-conformance-v0.1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("protocol is not math-cpu-gpu-conformance-v0.1.");
        }

        var cases = new List<ConformanceCase>();
        foreach (var element in root.GetProperty("cases").EnumerateArray())
        {
            var operation = element.GetProperty("operation");
            var contractFunction = new ContractFunction(
                RequiredString(operation, "identity"),
                RequiredString(operation, "ownerTypeName"),
                RequiredString(operation, "methodName"),
                ReadStrings(operation.GetProperty("parameterTypeNames")),
                operation.GetProperty("parameterTypeNames").EnumerateArray().Select(_ => "none").ToArray(),
                RequiredString(operation, "returnTypeName"),
                RequiredString(operation, "mapping"),
                string.Empty);
            var comparison = element.GetProperty("comparison");
            var dispositions = element.GetProperty("disposition");
            cases.Add(new ConformanceCase(
                RequiredString(element, "id"),
                contractFunction,
                element.GetProperty("inputs").EnumerateArray().Select(value => ReadValue(value)).ToArray(),
                ReadValue(element.GetProperty("expected")),
                new ComparisonProfile(
                    RequiredString(comparison, "name"),
                    comparison.GetProperty("absoluteTolerance").GetDouble(),
                    comparison.GetProperty("relativeTolerance").GetDouble(),
                    comparison.GetProperty("maxUlps").GetInt32()),
                ReadStrings(element.GetProperty("requiredCapabilities")),
                ReadStrings(element.GetProperty("stages")),
                RequiredString(dispositions, "cpu"),
                RequiredString(dispositions, "shader"),
                RequiredString(dispositions, "render")));
        }

        var coverage = root.GetProperty("coverage");
        var coverageData = new ConformanceCoverage(
            coverage.GetProperty("manifestFunctionCount").GetInt32(),
            coverage.GetProperty("supportedCount").GetInt32(),
            coverage.GetProperty("caseCount").GetInt32(),
            coverage.GetProperty("excludedCount").GetInt32(),
            coverage.GetProperty("unsupportedManifestCount").GetInt32());
        if (coverageData.CaseCount != cases.Count)
        {
            throw new InvalidOperationException("coverage.caseCount does not match cases length.");
        }

        return new ConformanceBundle(cases.ToArray(), coverageData);
    }

    private static ConformanceValue ReadValue(JsonElement element)
    {
        return new ConformanceValue(
            RequiredString(element, "type"),
            ReadStrings(element.GetProperty("words")));
    }

    private static string[] ReadStrings(JsonElement element)
    {
        return element.EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException("bundle string value is missing."))
            .ToArray();
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"bundle property '{propertyName}' is missing.");
    }

    private static bool IsFirstSliceFunction(ContractFunction function)
        => OperationNames.Contains(function.MethodName)
            && IsFloatSliceType(function.OwnerType)
            && FloatSliceTypes.Contains(function.ReturnType)
            && function.ParameterTypes.All(FloatSliceTypes.Contains);

    private static bool IsFloatSliceType(string typeName)
        => typeName == "maths" || FloatSliceTypes.Contains(typeName);

    private static string BuildFixtureSource(IReadOnlyList<ContractFunction> functions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using Delta.Maths;");
        builder.AppendLine("using Delta.Shader;");
        builder.AppendLine();
        builder.AppendLine("namespace Delta.Shader.MathsConformance.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class MathsConformanceFixtures");
        builder.AppendLine("{");
        for (var index = 0; index < functions.Count; index++)
        {
            AppendFixture(builder, functions[index], index);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendFixture(StringBuilder builder, ContractFunction function, int index)
    {
        var methodName = $"Case{index:0000}";
        var contextName = methodName + "Context";
        if (function.ParameterModifiers.Length != function.ParameterTypes.Length)
        {
            throw new InvalidOperationException(
                $"Manifest parameter modifier count does not match parameter count for {function.Identity}.");
        }

        var inputSlotByParameter = new int[function.ParameterTypes.Length];
        var outSlotByParameter = new int[function.ParameterTypes.Length];
        Array.Fill(inputSlotByParameter, -1);
        Array.Fill(outSlotByParameter, -1);
        var inputCount = 0;
        var outCount = 0;
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var modifier = function.ParameterModifiers[parameterIndex];
            if (modifier is "none" or "ref")
            {
                inputSlotByParameter[parameterIndex] = inputCount++;
            }
            else if (modifier == "out")
            {
                outSlotByParameter[parameterIndex] = outCount++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported parameter modifier '{modifier}' in {function.Identity}.");
            }
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"    public readonly struct {contextName}");
        builder.AppendLine("    {");
        var binding = 0;
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var inputSlot = inputSlotByParameter[parameterIndex];
            if (inputSlot < 0)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadOnlyStorageBuffer<{function.ParameterTypes[parameterIndex]}> Input{inputSlot};");
            binding++;
        }

        if (function.ReturnType != "void")
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadWriteStorageBuffer<{function.ReturnType}> Output;");
            binding++;
        }

        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var outSlot = outSlotByParameter[parameterIndex];
            if (outSlot < 0)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadWriteStorageBuffer<{function.ParameterTypes[parameterIndex]}> Out{outSlot};");
            binding++;
        }

        builder.AppendLine();
        builder.AppendLine("        [PushConstant]");
        builder.AppendLine("        public readonly uint Count;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    [ComputeShader(localSizeX: 64)]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public static void {methodName}(in {contextName} context)");
        builder.AppendLine("    {");
        builder.AppendLine("        uint index = ShaderBuiltins.GlobalInvocationId.X;");
        if (inputCount == 0)
        {
            builder.AppendLine("        if (index >= context.Count)");
        }
        else
        {
            builder.AppendLine("        if (index >= context.Count || index >= context.Input0.Length)");
        }
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        var arguments = new string[function.ParameterTypes.Length];
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var modifier = function.ParameterModifiers[parameterIndex];
            if (modifier == "out")
            {
                arguments[parameterIndex] = $"out out{outSlotByParameter[parameterIndex]}";
                builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ParameterTypes[parameterIndex]} out{outSlotByParameter[parameterIndex]};");
            }
            else
            {
                var inputSlot = inputSlotByParameter[parameterIndex];
                var inputExpression = $"context.Input{inputSlot}[index]";
                if (modifier == "ref")
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ParameterTypes[parameterIndex]} ref{inputSlot} = {inputExpression};");
                    arguments[parameterIndex] = $"ref ref{inputSlot}";
                }
                else
                {
                    arguments[parameterIndex] = inputExpression;
                }
            }
        }

        var operatorToken = GetOperatorToken(function);
        string expression;
        if (operatorToken is null)
        {
            expression = $"Delta.Maths.{function.OwnerType}.{function.MethodName}({string.Join(", ", arguments)})";
        }
        else if (arguments.Length == 1)
        {
            expression = $"({operatorToken}{arguments[0]})";
        }
        else if (arguments.Length == 2)
        {
            expression = $"({arguments[0]} {operatorToken} {arguments[1]})";
        }
        else
        {
            throw new InvalidOperationException(
                $"Operator {function.Identity} has {arguments.Length} operands.");
        }

        if (function.ReturnType == "void")
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        {expression};");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ReturnType} result = {expression};");
            builder.AppendLine("        context.Output[index] = result;");
        }

        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var outSlot = outSlotByParameter[parameterIndex];
            if (outSlot >= 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"        context.Out{outSlot}[index] = out{outSlot};");
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string? GetOperatorToken(ContractFunction function)
    {
        if (!function.MethodName.StartsWith("op_", StringComparison.Ordinal))
        {
            return null;
        }

        if (function.Mapping == "Builtin" && function.GlslName.Length != 0)
        {
            return function.GlslName;
        }

        return function.MethodName switch
        {
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_BitwiseAnd" => "&",
            "op_BitwiseOr" => "|",
            "op_ExclusiveOr" => "^",
            "op_LeftShift" => "<<",
            "op_RightShift" => ">>",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_UnaryNegation" => "-",
            "op_UnaryPlus" => "+",
            "op_OnesComplement" => "~",
            _ => null
        };
    }

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

    private static ToolResult RunTool(string? fileName, params string[] arguments)
    {
        if (fileName is null)
        {
            return new ToolResult(1, "External validation tool is not available.");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ToolResult(process.ExitCode, output);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record ContractFunction(
        string Identity,
        string OwnerType,
        string MethodName,
        string[] ParameterTypes,
        string[] ParameterModifiers,
        string ReturnType,
        string Mapping,
        string GlslName);

    private sealed record ConformanceValue(string Type, string[] Words);

    private sealed record ComparisonProfile(
        string Name,
        double AbsoluteTolerance,
        double RelativeTolerance,
        int MaxUlps);

    private sealed record ConformanceCase(
        string Id,
        ContractFunction Operation,
        IReadOnlyList<ConformanceValue> Inputs,
        ConformanceValue Expected,
        ComparisonProfile Comparison,
        IReadOnlyList<string> RequiredCapabilities,
        IReadOnlyList<string> Stages,
        string CpuDisposition,
        string ShaderDisposition,
        string RenderDisposition);

    private sealed record ConformanceCoverage(
        int ManifestFunctionCount,
        int SupportedCount,
        int CaseCount,
        int ExcludedCount,
        int UnsupportedManifestCount);

    private sealed record ConformanceBundle(
        ConformanceCase[] Cases,
        ConformanceCoverage Coverage);

    private sealed record ToolResult(int ExitCode, string Output);

    private sealed class ConformanceIndex
    {
        public int SchemaVersion { get; init; } = 1;
        public string ContractPath { get; init; } = string.Empty;
        public string BundlePath { get; init; } = string.Empty;
        public string FixtureSourcePath { get; init; } = string.Empty;
        public int SelectedCount { get; init; }
        public int BundleCaseCount { get; init; }
        public int ManifestFunctionCount { get; init; }
        public int SupportedCaseCount { get; init; }
        public int UnsupportedManifestCount { get; init; }
        public int ExcludedCaseCount { get; init; }
        public int ArtifactCount { get; init; }
        public int CompilerBlockedCount { get; init; }
        public int CapabilityBlockedCount { get; init; }
        public int BackendBlockedCount { get; init; }
        public int ExternalValidationBlockedCount { get; init; }
        public int MismatchedCount { get; init; }
        public int AccountedCount { get; init; }
        public IReadOnlyList<PublishedCase> Cases { get; init; } = Array.Empty<PublishedCase>();
    }

    private sealed class ResolvedAbiDocument
    {
        public string CaseId { get; init; } = string.Empty;
        public string OperationIdentity { get; init; } = string.Empty;
        public string EntryPointName { get; init; } = string.Empty;
        public Delta.Shader.Contract.ShaderStage Stage { get; init; }
        public string ArtifactPath { get; init; } = string.Empty;
        public required ResolvedShaderAbi Abi { get; init; }
    }

    private sealed class ResolvedShaderAbi
    {
        public Final.ShaderStage Stage { get; init; }
        public IReadOnlyList<ResolvedResourceBinding> Resources { get; init; } = Array.Empty<ResolvedResourceBinding>();
        public IReadOnlyList<ResolvedPushConstantRange> PushConstants { get; init; } = Array.Empty<ResolvedPushConstantRange>();
        public IReadOnlyList<Final.ShaderInterfaceVariable> Inputs { get; init; } = Array.Empty<Final.ShaderInterfaceVariable>();
        public IReadOnlyList<Final.ShaderInterfaceVariable> Outputs { get; init; } = Array.Empty<Final.ShaderInterfaceVariable>();
        public IReadOnlyList<Final.ShaderVertexInput> VertexInputs { get; init; } = Array.Empty<Final.ShaderVertexInput>();
        public IReadOnlyList<Final.ShaderVertexBufferLayout> VertexBuffers { get; init; } = Array.Empty<Final.ShaderVertexBufferLayout>();
        public IReadOnlyList<ResolvedSpecializationConstant> SpecializationConstants { get; init; } = Array.Empty<ResolvedSpecializationConstant>();
        public Final.ShaderWorkgroupSize WorkgroupSize { get; init; }
        public Final.ShaderCapabilities RequiredCapabilities { get; init; }

        public static ResolvedShaderAbi From(Final.ShaderAbi abi)
            => new()
            {
                Stage = abi.Stage,
                Resources = abi.Resources.Select(ResolvedResourceBinding.From).ToArray(),
                PushConstants = abi.PushConstants.Select(ResolvedPushConstantRange.From).ToArray(),
                Inputs = abi.Inputs,
                Outputs = abi.Outputs,
                VertexInputs = abi.VertexInputs,
                VertexBuffers = abi.VertexBuffers,
                SpecializationConstants = abi.SpecializationConstants
                    .Select(ResolvedSpecializationConstant.From)
                    .ToArray(),
                WorkgroupSize = abi.WorkgroupSize,
                RequiredCapabilities = abi.RequiredCapabilities
            };
    }

    private sealed class ResolvedResourceBinding
    {
        public Final.ShaderBinding Binding { get; init; }
        public Final.ShaderResourceKind Kind { get; init; }
        public Final.ShaderResourceAccess Access { get; init; }
        public Final.ShaderStageMask Stages { get; init; }
        public Final.ShaderAbiLayout Layout { get; init; } = Final.ShaderAbiLayout.Empty;
        public uint DescriptorCount { get; init; }

        public static ResolvedResourceBinding From(Final.ShaderResourceBinding resource)
            => new()
            {
                Binding = resource.Binding,
                Kind = resource.Kind,
                Access = resource.Access,
                Stages = resource.Stages,
                Layout = resource.Layout,
                DescriptorCount = resource.DescriptorCount
            };
    }

    private sealed class ResolvedPushConstantRange
    {
        public uint Offset { get; init; }
        public uint Size { get; init; }
        public Final.ShaderStageMask Stages { get; init; }
        public Final.ShaderAbiLayout Layout { get; init; } = Final.ShaderAbiLayout.Empty;

        public static ResolvedPushConstantRange From(Final.ShaderPushConstantRange pushConstant)
            => new()
            {
                Offset = pushConstant.Offset,
                Size = pushConstant.Size,
                Stages = pushConstant.Stages,
                Layout = pushConstant.Layout
            };
    }

    private sealed class ResolvedSpecializationConstant
    {
        public uint Id { get; init; }
        public Final.ShaderValueType Type { get; init; }
        public byte[] DefaultValue { get; init; } = Array.Empty<byte>();

        public static ResolvedSpecializationConstant From(Final.ShaderSpecializationConstant constant)
            => new()
            {
                Id = constant.Id,
                Type = constant.Type,
                DefaultValue = constant.DefaultValue.ToArray()
            };
    }

    private sealed class PublishedCase
    {
        public string CaseId { get; init; } = string.Empty;
        public string SourceCaseId { get; init; } = string.Empty;
        public string OperationIdentity { get; init; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ArtifactPath { get; set; }
        public string? AbiPath { get; set; }
        public string? Diagnostic { get; set; }
        public IReadOnlyList<ConformanceValue> Inputs { get; init; } = Array.Empty<ConformanceValue>();
        public ConformanceValue Expected { get; init; } = new(string.Empty, Array.Empty<string>());
        public ComparisonProfile Comparison { get; init; } = new(string.Empty, 0, 0, 0);
        public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();
        public string CpuDisposition { get; init; } = string.Empty;
        public string ShaderDisposition { get; init; } = string.Empty;
        public string RenderDisposition { get; init; } = string.Empty;

        public static PublishedCase FromCase(ConformanceCase conformanceCase, string caseId)
            => new()
            {
                CaseId = caseId,
                SourceCaseId = conformanceCase.Id,
                OperationIdentity = conformanceCase.Operation.Identity,
                Inputs = conformanceCase.Inputs,
                Expected = conformanceCase.Expected,
                Comparison = conformanceCase.Comparison,
                RequiredCapabilities = conformanceCase.RequiredCapabilities,
                Stages = conformanceCase.Stages,
                CpuDisposition = conformanceCase.CpuDisposition,
                ShaderDisposition = conformanceCase.ShaderDisposition,
                RenderDisposition = conformanceCase.RenderDisposition
            };
    }
}
