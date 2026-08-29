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

        var fragmentPush = Assert.Single(roundedFragment.BuildManifest.PushConstants);
        Assert.Equal("main", roundedFragment.BuildManifest.EntryPointName);
        Assert.Empty(roundedFragment.BuildManifest.Resources);
        Assert.Equal(80u, fragmentPush.Size);
        Assert.Equal(16u, fragmentPush.Alignment);
        Assert.Equal(0u, Assert.Single(fragmentPush.Members, member => member.Name == "Resolution").Offset);
        Assert.Equal(16u, Assert.Single(fragmentPush.Members, member => member.Name == "Rect").Offset);
        Assert.Equal(32u, Assert.Single(fragmentPush.Members, member => member.Name == "FillColor").Offset);
        Assert.Equal(48u, Assert.Single(fragmentPush.Members, member => member.Name == "BorderColor").Offset);
        Assert.Equal(64u, Assert.Single(fragmentPush.Members, member => member.Name == "CornerRadius").Offset);
        Assert.Equal(68u, Assert.Single(fragmentPush.Members, member => member.Name == "BorderWidth").Offset);

        var fragmentGlsl = GlslEmitter.EmitFromModule(fragmentModule).Source;
        Assert.Contains("#version 460", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("smoothstep", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("BorderColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("FillColor", fragmentGlsl, StringComparison.Ordinal);
        Assert.Equal("main", vertexManifest.EntryPointName);
        Assert.Single(vertexManifest.Outputs, output => output.Builtin == "Position");
        Assert.Single(vertexManifest.Outputs, output => output.Name == "Uv");
    }

    private static async Task<Compilation> LoadUiCompilationAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "DeltaShader.Ui", "DeltaShader.Ui.csproj");
        Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(true);
        Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(true);
        return compilation ?? throw new InvalidOperationException("DeltaShader.Ui compilation was not created.");
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
