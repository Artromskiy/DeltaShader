using Delta.Shader.Contract;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void ArtifactOwnsReadOnlySpirvAndManifestState()
    {
        var spirv = ValidSpirv();
        var abi = new ShaderAbi(
            ShaderStage.Compute,
            resources: new[] { new ShaderResourceBinding(new ShaderBinding(0, 2), ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Read, ShaderStageMask.Compute, new ShaderAbiLayout(4, 4, arrayStride: 4)) },
            workgroupSize: new ShaderWorkgroupSize(1, 1, 1));
        var artifact = new ShaderArtifact(spirv, "main", abi);

        spirv[0] = 9;
        var upload = artifact.CopySpirv();
        upload[1] = 8;

        Assert.Equal(new byte[] { 3, 2, 35, 7 }, artifact.Spirv.ToArray());
        Assert.Equal(ShaderResourceKind.StorageBuffer, artifact.Abi.Resources[0].Kind);
        Assert.Equal(new byte[] { 3, 2, 35, 7 }, artifact.CopySpirv());
    }

    [Fact]
    public void GraphicsProgramRejectsMismatchedSharedResourceLayout()
    {
        var vertex = Artifact(ShaderStage.Vertex, new ShaderResourceBinding(new ShaderBinding(0, 1), ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Read, ShaderStageMask.Vertex, new ShaderAbiLayout(16, 16)));
        var fragment = Artifact(ShaderStage.Fragment, new ShaderResourceBinding(new ShaderBinding(0, 1), ShaderResourceKind.SampledTexture, ShaderResourceAccess.Read, ShaderStageMask.Fragment));

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

    private static ShaderArtifact Artifact(ShaderStage stage, ShaderResourceBinding? resource = null, uint pushSize = 0)
    {
        var pushConstants = pushSize == 0
            ? Array.Empty<ShaderPushConstantRange>()
            : new[] { new ShaderPushConstantRange(0, pushSize, ShaderStageMask.AllGraphics, new ShaderAbiLayout(pushSize, 16)) };
        var abi = new ShaderAbi(stage, resource is null ? Array.Empty<ShaderResourceBinding>() : new[] { resource }, pushConstants, workgroupSize: stage == ShaderStage.Compute ? new ShaderWorkgroupSize(1, 1, 1) : default);
        return new ShaderArtifact(ValidSpirv(), "main", abi);
    }

    private static byte[] ValidSpirv() => new byte[] { 3, 2, 35, 7 };
}
