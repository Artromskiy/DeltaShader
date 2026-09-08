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
        Assert.NotEmpty(results);
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
        Assert.Equal(48u, vertexResource.Size);
        Assert.Equal(48u, vertexResource.ArrayStride);
        Assert.Equal(0u, Assert.Single(vertexResource.Members, member => member.Name == "Rect").Offset);
        Assert.Equal(16u, Assert.Single(vertexResource.Members, member => member.Name == "FillColor").Offset);
        Assert.Equal(32u, Assert.Single(vertexResource.Members, member => member.Name == "CornerRadii").Offset);

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
        Assert.Contains("fragColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("c.xyz * c.w", solidFragmentGlsl, StringComparison.Ordinal);
        Assert.Equal("main", vertexManifest.EntryPointName);
        Assert.Single(vertexManifest.Outputs, output => output.Builtin == "Position");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Uv");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Rect");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "FillColor");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "CornerRadii");
        var packedUvOutput = Assert.Single(vertexManifest.Outputs, output => output.Name == "Uv");
        Assert.Equal("vec2", packedUvOutput.GlslType);
    }

    [Fact]
    public async Task CanonicalInnerVisualEffects_CompileAsFiniteVariants()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.Contains(results, item => item.EntryPointName == "analytic-rounded-rectangle");

        foreach (var entryPoint in new[] { "rounded-inner-shadow" })
        {
            var vertex = Assert.Single(results, item => item.EntryPointName == entryPoint && item.Module?.Stage == ShaderStage.Vertex);
            var fragment = Assert.Single(results, item => item.EntryPointName == entryPoint && item.Module?.Stage == ShaderStage.Fragment);
            Assert.True(vertex.Success, string.Join(Environment.NewLine, vertex.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.Equal(96u, Assert.Single(vertex.BuildManifest!.Resources).ArrayStride);
            Assert.Equal(8u, Assert.Single(vertex.BuildManifest.PushConstants).Size);
            Assert.Empty(fragment.BuildManifest!.PushConstants);
            Assert.Contains("smoothstep", GlslEmitter.EmitFromModule(fragment.Module!).Source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CachedMaskRoundedRectangle_CompilesWithStorageAndSampledMaskAbi()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        var results = ShaderCompiler.CompileAll(compilation);
        var vertex = Assert.Single(
            results,
            result => result.EntryPointName == "cached-mask-rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Vertex);
        var fragment = Assert.Single(
            results,
            result => result.EntryPointName == "cached-mask-rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Fragment);

        Assert.True(vertex.Success, string.Join(Environment.NewLine, vertex.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(fragment.Success, string.Join(Environment.NewLine, fragment.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var vertexManifest = vertex.BuildManifest ?? throw new InvalidOperationException("Cached-mask vertex manifest is missing.");
        var fragmentManifest = fragment.BuildManifest ?? throw new InvalidOperationException("Cached-mask fragment manifest is missing.");
        var instance = Assert.Single(vertexManifest.Resources);
        var mask = Assert.Single(fragmentManifest.Resources);

        Assert.Equal("storage-buffer", instance.Category);
        Assert.Equal(0u, instance.Set);
        Assert.Equal(0u, instance.Binding);
        Assert.Equal(48u, instance.Size);
        Assert.Equal(48u, instance.ArrayStride);
        Assert.Equal("sampled-texture", mask.Category);
        Assert.Equal(0u, mask.Set);
        Assert.Equal(1u, mask.Binding);
        Assert.Equal(8u, Assert.Single(vertexManifest.PushConstants).Size);
        Assert.Empty(fragmentManifest.PushConstants);

        var fragmentGlsl = GlslEmitter.EmitFromModule(fragment.Module!).Source;
        Assert.Contains("MaskUv", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("texture", fragmentGlsl, StringComparison.Ordinal);
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
        Assert.Contains("PackInnerShadowRoundedRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackInnerShadowRoundedRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InsetShadow", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackCachedMaskRoundedRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackCachedMaskRoundedRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
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
