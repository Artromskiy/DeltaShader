using Delta.Shader.Analyzers;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class UiShaderTests
{
    [Fact]
    public async Task CanonicalUiRectangles_CompileWithResolvedPushConstantAbi()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);

        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(8, results.Count);
        Assert.All(results, result => Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        ShaderCompilationResult roundedFragment = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Fragment);
        ShaderCompilationResult solidFragment = Assert.Single(
            results,
            result => result.EntryPointName == "solid-rectangle" &&
                result.Module?.Stage == ShaderStage.Fragment);
        ShaderCompilationResult roundedVertex = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Vertex);

        var fragmentModule = roundedFragment.Module;
        var fragmentManifest = roundedFragment.BuildManifest;
        var solidFragmentModule = solidFragment.Module;
        if (fragmentModule is null || fragmentManifest is null || solidFragmentModule is null)
        {
            throw new InvalidOperationException("Rounded rectangle compilation did not produce a module and manifest.");
        }

        var vertexManifest = roundedVertex.BuildManifest;
        if (vertexManifest is null)
        {
            throw new InvalidOperationException("Rounded rectangle vertex compilation did not produce a manifest.");
        }

        var vertexResource = Assert.Single(vertexManifest.Resources);
        Assert.Equal("storage-buffer", vertexResource.Category);
        Assert.Equal(ShaderResourceAccess.ReadOnly, vertexResource.Access);
        Assert.Equal(ShaderStage.Vertex, vertexResource.Stage);
        Assert.Equal(0u, vertexResource.Set);
        Assert.Equal(0u, vertexResource.Binding);
        Assert.Equal(16u, vertexResource.Alignment);
        Assert.Equal(80u, vertexResource.Size);
        Assert.Equal(80u, vertexResource.ArrayStride);
        Assert.Equal(0u, Assert.Single(vertexResource.Members, member => member.Name == "Rect").Offset);
        Assert.Equal(16u, Assert.Single(vertexResource.Members, member => member.Name == "FillColor").Offset);
        Assert.Equal(32u, Assert.Single(vertexResource.Members, member => member.Name == "BorderColor").Offset);
        Assert.Equal(48u, Assert.Single(vertexResource.Members, member => member.Name == "CornerRadii").Offset);
        Assert.Equal(64u, Assert.Single(vertexResource.Members, member => member.Name == "BorderWidth").Offset);

        var vertexPush = Assert.Single(vertexManifest.PushConstants);
        Assert.Equal("main", fragmentManifest.EntryPointName);
        Assert.Empty(fragmentManifest.Resources);
        Assert.Equal(8u, vertexPush.Size);
        Assert.Equal(8u, vertexPush.Alignment);
        Assert.Equal(0u, Assert.Single(vertexPush.Members, member => member.Name == "Resolution").Offset);
        Assert.Empty(fragmentManifest.PushConstants);

        var fragmentGlsl = GlslEmitter.EmitFromModule(fragmentModule).Source;
        var solidFragmentGlsl = GlslEmitter.EmitFromModule(solidFragmentModule).Source;
        var vertexGlsl = GlslEmitter.EmitFromModule(roundedVertex.Module!).Source;
        Assert.Contains("gl_InstanceIndex", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("#version 460", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("borderCoverage", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("outerCoverage", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("cornerRadii", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("d.xzzx", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("d.yyww", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("influence", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("hasMax", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("notMax", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("isRightTop", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("winner", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("dot", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("center", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("1.0 - smoothstep(-edge, edge, distance)", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("1.0 - smoothstep(-edge, edge, distance +", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("borderCoverage", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fragColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("c.xyz * c.w", solidFragmentGlsl, StringComparison.Ordinal);
        Assert.Equal("main", vertexManifest.EntryPointName);
        Assert.Single(vertexManifest.Outputs, output => output.Builtin == "Position");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Uv");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Rect");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "FillColor");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "BorderColor");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "CornerRadii");
        var packedUvOutput = Assert.Single(vertexManifest.Outputs, output => output.Name == "Uv");
        Assert.Equal("vec4", packedUvOutput.GlslType);
    }

    [Fact]
    public async Task AnalyticVisualEffects_CompileWithNestedTypedInstanceAbi()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        var result = Assert.Single(
            results,
            item => item.EntryPointName == "analytic-rounded-rectangle" &&
                item.Module?.Stage == ShaderStage.Vertex);
        var fragment = Assert.Single(
            results,
            item => item.EntryPointName == "analytic-rounded-rectangle" &&
                item.Module?.Stage == ShaderStage.Fragment);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));

        var vertexManifest = result.BuildManifest;
        var fragmentManifest = fragment.BuildManifest;
        if (vertexManifest is null || fragmentManifest is null)
        {
            throw new InvalidOperationException("Analytic visual effect compilation did not produce manifests.");
        }

        var vertexResource = Assert.Single(vertexManifest.Resources);
        Assert.Equal(240u, vertexResource.Size);
        Assert.Equal(240u, vertexResource.ArrayStride);
        Assert.Equal(ShaderStage.Vertex, vertexResource.Stage);
        Assert.Empty(fragmentManifest.Resources);

        Assert.Equal(48u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeColor").Offset);
        Assert.Equal(64u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeOffset").Offset);
        Assert.Equal(72u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeWidth").Offset);
        Assert.Equal(76u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeBlurRadius").Offset);
        Assert.Equal(80u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeSpread").Offset);
        Assert.Equal(84u, Assert.Single(vertexResource.Members, member => member.Name == "StrokeIntensity").Offset);
        Assert.Equal(96u, Assert.Single(vertexResource.Members, member => member.Name == "OuterShadowColor").Offset);
        Assert.Equal(112u, Assert.Single(vertexResource.Members, member => member.Name == "OuterShadowOffset").Offset);
        Assert.Equal(144u, Assert.Single(vertexResource.Members, member => member.Name == "InsetShadowColor").Offset);
        Assert.Equal(160u, Assert.Single(vertexResource.Members, member => member.Name == "InsetShadowOffset").Offset);
        Assert.Equal(192u, Assert.Single(vertexResource.Members, member => member.Name == "GlowColor").Offset);
        Assert.Equal(208u, Assert.Single(vertexResource.Members, member => member.Name == "GlowOffset").Offset);

        Assert.Equal(8u, Assert.Single(vertexManifest.PushConstants).Size);
        Assert.Empty(fragmentManifest.PushConstants);

        var vertexGlsl = GlslEmitter.EmitFromModule(result.Module!).Source;
        var fragmentGlsl = GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("gl_InstanceIndex", vertexGlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("gl_InstanceIndex", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("exp", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("InsetShadow", fragmentGlsl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedUiFactoriesExposeDirectPushRootPackers()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        compilation = RemovePersistedGeneratedSources(compilation);
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DeltaGraphicsGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var generatedCompilation,
            out var generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            generatedCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources)
                .Select(result => result.SourceText.ToString()));

        Assert.Contains("PackSolidRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackSolidRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackAnalyticRoundedRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackAnalyticRoundedRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackSolidRectangleVertexFrame", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackSolidRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackRoundedRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
    }

    private static Compilation RemovePersistedGeneratedSources(Compilation compilation)
    {
        SyntaxTree[] generatedTrees = compilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return compilation.RemoveSyntaxTrees(generatedTrees);
    }

    private static async Task<Compilation> LoadUiCompilationAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        string projectPath = Path.GetFullPath(Path.Combine(
            FindRepositoryRoot(), "..", "DeltaRender", "tools", "DeltaRender.UIShaders", "DeltaRender.UIShaders.csproj"));
        Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(true);
        Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(true);
        return compilation ?? throw new InvalidOperationException("DeltaShader.UI compilation was not created.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeltaShader.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("DeltaShader repository root was not found.");
    }
}
