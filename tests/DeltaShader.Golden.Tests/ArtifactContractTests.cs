using Contract = Delta.Shader.Contract;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void ArtifactOwnsReadOnlySpirvAndManifestState()
    {
        var spirv = ValidSpirv();
        var abi = new Contract.ShaderAbi(
            Contract.ShaderStage.Compute,
            resources: new[] { new Contract.ShaderResourceBinding(new Contract.ShaderBinding(0, 2), Contract.ShaderResourceKind.StorageBuffer, Contract.ShaderResourceAccess.Read, Contract.ShaderStageMask.Compute, new Contract.ShaderAbiLayout(4, 4, arrayStride: 4)) },
            workgroupSize: new Contract.ShaderWorkgroupSize(1, 1, 1));
        var artifact = new Contract.ShaderArtifact(spirv, "main", abi);

        spirv[0] = 9;
        var upload = artifact.CopySpirv();
        upload[1] = 8;

        Assert.Equal(new byte[] { 3, 2, 35, 7 }, artifact.Spirv.ToArray());
        Assert.Equal(Contract.ShaderResourceKind.StorageBuffer, artifact.Abi.Resources[0].Kind);
        Assert.Equal(new byte[] { 3, 2, 35, 7 }, artifact.CopySpirv());
    }

    [Fact]
    public void GraphicsProgramRejectsMismatchedSharedResourceLayout()
    {
        var vertex = Artifact(Contract.ShaderStage.Vertex, new Contract.ShaderResourceBinding(new Contract.ShaderBinding(0, 1), Contract.ShaderResourceKind.StorageBuffer, Contract.ShaderResourceAccess.Read, Contract.ShaderStageMask.Vertex, new Contract.ShaderAbiLayout(16, 16)));
        var fragment = Artifact(Contract.ShaderStage.Fragment, new Contract.ShaderResourceBinding(new Contract.ShaderBinding(0, 1), Contract.ShaderResourceKind.SampledTexture, Contract.ShaderResourceAccess.Read, Contract.ShaderStageMask.Fragment));

        Assert.Throws<ArgumentException>(() => new Contract.GraphicsShaderProgram(vertex, fragment));
    }

    [Fact]
    public void GraphicsProgramRejectsMismatchedPushConstantLayout()
    {
        var vertex = Artifact(Contract.ShaderStage.Vertex, pushSize: 16);
        var fragment = Artifact(Contract.ShaderStage.Fragment, pushSize: 32);

        Assert.Throws<ArgumentException>(() => new Contract.GraphicsShaderProgram(vertex, fragment));
    }

    [Fact]
    public void GraphicsProgramRejectsWrongStageBeforeConsumption()
    {
        var compute = Artifact(Contract.ShaderStage.Compute);
        var fragment = Artifact(Contract.ShaderStage.Fragment);

        Assert.Throws<ArgumentException>(() => new Contract.GraphicsShaderProgram(compute, fragment));
    }

    private static Contract.ShaderArtifact Artifact(Contract.ShaderStage stage, Contract.ShaderResourceBinding? resource = null, uint pushSize = 0)
    {
        var pushConstants = pushSize == 0
            ? Array.Empty<Contract.ShaderPushConstantRange>()
            : new[] { new Contract.ShaderPushConstantRange(0, pushSize, Contract.ShaderStageMask.AllGraphics, new Contract.ShaderAbiLayout(pushSize, 16)) };
        var abi = new Contract.ShaderAbi(stage, resource is null ? Array.Empty<Contract.ShaderResourceBinding>() : new[] { resource }, pushConstants, workgroupSize: stage == Contract.ShaderStage.Compute ? new Contract.ShaderWorkgroupSize(1, 1, 1) : default);
        return new Contract.ShaderArtifact(ValidSpirv(), "main", abi);
    }

    private static byte[] ValidSpirv() => new byte[] { 3, 2, 35, 7 };
}
