using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
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
        Assert.Equal(4, results.Count);
        Assert.All(results, result => Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message))));

        ShaderCompilationResult roundedFragment = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Fragment);
        ShaderCompilationResult roundedVertex = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle" &&
                result.Module?.Stage == ShaderStage.Vertex);

        var fragmentModule = roundedFragment.Module;
        if (fragmentModule is null || roundedFragment.BuildManifest is null)
        {
            throw new InvalidOperationException("Rounded rectangle compilation did not produce a module and manifest.");
        }

        var vertexManifest = roundedVertex.BuildManifest;
        if (vertexManifest is null)
        {
            throw new InvalidOperationException("Rounded rectangle vertex compilation did not produce a manifest.");
        }

        var vertexResource = Assert.Single(roundedVertex.BuildManifest.Resources);
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

        var vertexPush = Assert.Single(roundedVertex.BuildManifest.PushConstants);
        Assert.Equal("main", roundedFragment.BuildManifest.EntryPointName);
        Assert.Empty(roundedFragment.BuildManifest.Resources);
        Assert.Equal(8u, vertexPush.Size);
        Assert.Equal(8u, vertexPush.Alignment);
        Assert.Equal(0u, Assert.Single(vertexPush.Members, member => member.Name == "Resolution").Offset);
        Assert.Empty(roundedFragment.BuildManifest.PushConstants);

        var fragmentGlsl = GlslEmitter.EmitFromModule(fragmentModule).Source;
        var vertexGlsl = GlslEmitter.EmitFromModule(roundedVertex.Module!).Source;
        Assert.Contains("gl_InstanceIndex", vertexGlsl, StringComparison.Ordinal);
        Assert.Contains("#version 460", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("BorderColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("FillColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("1 - smoothstep(-edge, edge, distance)", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("1 - smoothstep(-edge, edge, distance +", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("max(fillCoverage - innerCoverage, 0)", fragmentGlsl, StringComparison.Ordinal);
        Assert.Equal("main", vertexManifest.EntryPointName);
        Assert.Single(vertexManifest.Outputs, output => output.Builtin == "Position");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Uv");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "Rect");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "FillColor");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "BorderColor");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "CornerRadii");
        Assert.Contains(vertexManifest.Outputs, output => output.Name == "BorderWidth");
    }

    [Fact]
    public async Task GeneratedUiFactoriesExposeDirectPushRootPackers()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = string.Join(
            Environment.NewLine,
            compilation.SyntaxTrees.Select(tree => tree.GetText().ToString()));

        Assert.Contains("PackSolidRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackSolidRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackSolidRectangleVertexFrame", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleVertexFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackSolidRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackRoundedRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(0u, value.Resolution.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(0u, value.Rect.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(16u, value.FillColor.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(48u, value.CornerRadii.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(52u, value.CornerRadii.y)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(56u, value.CornerRadii.z)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(60u, value.CornerRadii.w)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(64u, value.BorderWidth)", generatedSource, StringComparison.Ordinal);
    }

    private static async Task<Compilation> LoadUiCompilationAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "DeltaShader.UI", "DeltaShader.UI.csproj");
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
