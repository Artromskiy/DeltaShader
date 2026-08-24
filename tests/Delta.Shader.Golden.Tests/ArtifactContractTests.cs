using Delta.Shader.Abstractions;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void ArtifactOwnsReadOnlySpirvAndManifestState()
    {
        var spirv = new byte[] { 1, 2, 3, 4 };
        var resources = new[] { new ShaderAbiResource { Set = 0, Binding = 2, Category = "storage-buffer", GlslType = "uint" } };
        var manifest = Manifest(ShaderStage.Compute, resources);
        var artifact = new ShaderArtifact(spirv, manifest);

        spirv[0] = 9;
        resources[0] = new ShaderAbiResource { Set = 0, Binding = 2, Category = "sampled-texture-2d", GlslType = "sampler2D" };
        var upload = artifact.GetSpirvForUpload();
        upload[1] = 8;

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, artifact.Spirv.ToArray());
        Assert.Equal("storage-buffer", artifact.Manifest.Resources[0].Category);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, artifact.GetSpirvForUpload());
    }

    [Fact]
    public void GraphicsProgramRejectsMismatchedSharedResourceLayout()
    {
        var vertex = Artifact(ShaderStage.Vertex, new ShaderAbiResource
        {
            Set = 0,
            Binding = 1,
            Category = "storage-buffer",
            GlslType = "VertexData",
            Size = 16,
            Alignment = 16
        });
        var fragment = Artifact(ShaderStage.Fragment, new ShaderAbiResource
        {
            Set = 0,
            Binding = 1,
            Category = "sampled-texture-2d",
            GlslType = "sampler2D"
        });

        Assert.Throws<ArgumentException>(() => new GraphicsShaderProgram(vertex, fragment));
    }

    [Fact]
    public void GraphicsProgramRejectsMismatchedPushConstantLayout()
    {
        var vertex = Artifact(ShaderStage.Vertex, pushSize: 16);
        var fragment = Artifact(ShaderStage.Fragment, pushSize: 32);

        Assert.Throws<ArgumentException>(() => new GraphicsShaderProgram(vertex, fragment));
    }

    [Fact]
    public void GraphicsProgramRejectsWrongStageBeforeConsumption()
    {
        var compute = Artifact(ShaderStage.Compute);
        var fragment = Artifact(ShaderStage.Fragment);

        Assert.Throws<ArgumentException>(() => new GraphicsShaderProgram(compute, fragment));
    }

    private static ShaderArtifact Artifact(ShaderStage stage, ShaderAbiResource? resource = null, uint pushSize = 0)
    {
        var pushConstants = pushSize == 0
            ? Array.Empty<ShaderAbiPushConstant>()
            : new[] { new ShaderAbiPushConstant { Name = "Constants", GlslType = "Constants", Alignment = 16, Size = pushSize } };
        return new ShaderArtifact(new byte[] { 1, 2, 3, 4 }, Manifest(stage, resource is null ? Array.Empty<ShaderAbiResource>() : new[] { resource }, pushConstants));
    }

    private static ShaderAbiManifest Manifest(ShaderStage stage, IReadOnlyList<ShaderAbiResource> resources, IReadOnlyList<ShaderAbiPushConstant>? pushConstants = null)
        => new()
        {
            Stage = stage,
            SourceEntryPointName = stage.ToString(),
            EntryPointName = "main",
            Resources = resources,
            PushConstants = pushConstants ?? Array.Empty<ShaderAbiPushConstant>()
        };
}
