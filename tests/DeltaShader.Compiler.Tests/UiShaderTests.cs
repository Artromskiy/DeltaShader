using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.UI;
using Delta.Maths;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class UiShaderTests
{
    [Fact]
    public void RoundedRectangleSliceBuilder_NormalizesRadiiAndPreservesNineSliceData()
    {
        var source = new RoundedRectangleParameters(
            new float4(10f, 20f, 100f, 100f),
            new float4(1f, 0f, 0f, 1f),
            new float4(0f, 0f, 0f, 1f),
            new float4(40f, 20f, 20f, 40f),
            4f);
        RoundedRectangleSliceParameters[] slices = new RoundedRectangleSliceParameters[9];

        int count = RoundedRectangleSliceBuilder.Build(in source, slices);

        Assert.Equal(9, count);
        Assert.Equal(40f, slices[5].CornerData.z);
        Assert.Equal(20f, slices[6].CornerData.z);
        Assert.Equal(20f, slices[7].CornerData.z);
        Assert.Equal(40f, slices[8].CornerData.z);
        Assert.Equal(1f, slices[5].CornerData.w);
        Assert.Equal(40f, slices[0].SegmentRect.z);
        Assert.Equal(20f, slices[0].SegmentRect.w);
        Assert.All(slices.Take(5), slice => Assert.Equal(0f, slice.CornerData.w));
        Assert.Equal(4f, slices[0].BorderWidth);
    }

    [Fact]
    public void RoundedRectangleSliceBuilder_LargeRadiiAreUniformlyNormalized()
    {
        var source = new RoundedRectangleParameters(
            new float4(0f, 0f, 100f, 50f),
            new float4(1f, 1f, 1f, 1f),
            new float4(0f, 0f, 0f, 1f),
            new float4(80f, 80f, 80f, 80f),
            2f);
        RoundedRectangleSliceParameters[] slices = new RoundedRectangleSliceParameters[9];

        int count = RoundedRectangleSliceBuilder.Build(in source, slices);

        Assert.True(count > 0);
        Assert.Equal(25f, slices[count - 1].CornerData.z);
        Assert.Equal(25f, slices[count - 1].CornerData.x - slices[count - 1].SegmentRect.x);
    }

    [Fact]
    public void RoundedRectangleSliceBuilder_ZeroRadiiCollapsesToInterior()
    {
        var source = new RoundedRectangleParameters(
            new float4(0f, 0f, 100f, 50f),
            new float4(1f, 1f, 1f, 1f),
            new float4(0f, 0f, 0f, 1f),
            new float4(0f, 0f, 0f, 0f),
            0f);
        RoundedRectangleSliceParameters[] slices = new RoundedRectangleSliceParameters[9];

        int count = RoundedRectangleSliceBuilder.Build(in source, slices);

        Assert.Equal(1, count);
        Assert.Equal(100f, slices[0].SegmentRect.z);
        Assert.Equal(50f, slices[0].SegmentRect.w);
        Assert.Equal(0f, slices[0].CornerData.w);
    }

    [Fact]
    public async Task CanonicalUiRectangles_CompileWithResolvedPushConstantAbi()
    {
        Compilation compilation = await LoadUiCompilationAsync().ConfigureAwait(true);
        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);

        IReadOnlyList<ShaderCompilationResult> results = ShaderCompiler.CompileAll(compilation);
        Assert.Equal(6, results.Count);
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

        ShaderCompilationResult sliceVertex = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle-slice" &&
                result.Module?.Stage == ShaderStage.Vertex);
        ShaderCompilationResult sliceFragment = Assert.Single(
            results,
            result => result.EntryPointName == "rounded-rectangle-slice" &&
                result.Module?.Stage == ShaderStage.Fragment);
        var sliceResource = Assert.Single(sliceVertex.BuildManifest!.Resources);
        Assert.Equal(96u, sliceResource.Size);
        Assert.Equal(96u, sliceResource.ArrayStride);
        Assert.DoesNotContain(sliceResource.Members, member => member.Name == "Rect");
        Assert.Equal(0u, Assert.Single(sliceResource.Members, member => member.Name == "FillColor").Offset);
        Assert.Equal(16u, Assert.Single(sliceResource.Members, member => member.Name == "BorderColor").Offset);
        Assert.Equal(32u, Assert.Single(sliceResource.Members, member => member.Name == "CornerRadii").Offset);
        Assert.Equal(48u, Assert.Single(sliceResource.Members, member => member.Name == "SegmentRect").Offset);
        Assert.Equal(64u, Assert.Single(sliceResource.Members, member => member.Name == "CornerData").Offset);
        Assert.Equal(80u, Assert.Single(sliceResource.Members, member => member.Name == "BorderWidth").Offset);
        Assert.Equal(8u, Assert.Single(sliceVertex.BuildManifest.PushConstants).Size);
        Assert.Empty(sliceFragment.BuildManifest!.Resources);
        Assert.Empty(sliceFragment.BuildManifest.PushConstants);

        var sliceFragmentGlsl = GlslEmitter.EmitFromModule(sliceFragment.Module!).Source;
        Assert.Contains("CornerData", sliceFragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("Pixel", sliceFragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("SegmentRect", sliceFragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("BorderWidth", sliceFragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("isCorner", sliceFragmentGlsl, StringComparison.Ordinal);
        Assert.Contains("fwidth", sliceFragmentGlsl, StringComparison.Ordinal);
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
        Assert.Contains("PackRoundedRectangleSliceVertexInstancesElement", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleSliceVertexInstancesElements", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PackRoundedRectangleSliceVertexFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackSolidRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PackRoundedRectangleFragmentFrame", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(0u, value.Resolution.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(0u, value.Rect.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(0u, value.FillColor.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(16u, value.FillColor.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(48u, value.CornerRadii.x)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(52u, value.CornerRadii.y)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(56u, value.CornerRadii.z)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(60u, value.CornerRadii.w)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(64u, value.BorderWidth)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("WriteFloat(80u, value.BorderWidth)", generatedSource, StringComparison.Ordinal);
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
