using Contract = Delta.Shader.Contract;
using Xunit;

namespace Delta.Shader.Golden.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void ArtifactOwnsReadOnlySpirvAndManifestState()
    {
        var expected = ValidSpirv();
        var spirv = expected.ToArray();
        var abi = new Contract.ShaderAbi(
            Contract.ShaderStage.Compute,
            resources: new[] { new Contract.ShaderResourceBinding(new Contract.ShaderBinding(0, 2), Contract.ShaderResourceKind.StorageBuffer, Contract.ShaderResourceAccess.Read, Contract.ShaderStageMask.Compute, new Contract.ShaderAbiLayout(4, 4, arrayStride: 4)) },
            workgroupSize: new Contract.ShaderWorkgroupSize(1, 1, 1));
        var artifact = new Contract.ShaderArtifact(spirv, "main", abi);

        spirv[0] = 9;
        var upload = artifact.CopySpirv();
        upload[1] = 8;

        Assert.Equal(expected, artifact.Spirv.ToArray());
        Assert.Equal(Contract.ShaderResourceKind.StorageBuffer, artifact.Abi.Resources[0].Kind);
        Assert.Equal(expected, artifact.CopySpirv());
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

    [Fact]
    public void ArtifactRejectsIncompleteOrInvalidSpirvHeaders()
    {
        var abi = ComputeAbi();
        var invalidModules = new[]
        {
            Array.Empty<byte>(),
            new byte[4],
            new byte[8],
            new byte[12],
            Header(version: 0x00020000),
            Header(bound: 0),
            Header(reserved: 1)
        };

        foreach (var invalidModule in invalidModules)
        {
            Assert.Throws<ArgumentException>(() => new Contract.ShaderArtifact(invalidModule, "main", abi));
        }
    }

    [Fact]
    public void AbiRejectsImpossibleLayouts()
    {
        var scalar = new Contract.ShaderValueType(Contract.ShaderValueKind.FloatingPoint, 32);
        var member = new Contract.ShaderAbiMember(scalar, 0, 4, 4);
        var secondMember = new Contract.ShaderAbiMember(scalar, 4, 4, 4);

        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbiLayout(4, 0));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbiLayout(4, 4, arrayStride: 2));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbiLayout(4, 4, members: new[]
        {
            new Contract.ShaderAbiMember(scalar, 4, 4, 4)
        }));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbiLayout(8, 4, members: new[] { member, member }));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbiLayout(8, 4, members: new[]
        {
            new Contract.ShaderAbiMember(Contract.ShaderValueType.Structure, 0, 4, 4, nestedLayout: new Contract.ShaderAbiLayout(8, 8))
        }));
        Assert.Equal(8u, new Contract.ShaderAbiLayout(8, 4, members: new[] { member, secondMember }).Size);
    }

    [Fact]
    public void AbiRejectsStageMismatchDuplicateBindingsAndInterfaces()
    {
        var fragmentStorage = new Contract.ShaderResourceBinding(
            new Contract.ShaderBinding(0, 0),
            Contract.ShaderResourceKind.StorageBuffer,
            Contract.ShaderResourceAccess.Read,
            Contract.ShaderStageMask.Fragment);

        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbi(Contract.ShaderStage.Vertex, resources: new[] { fragmentStorage }));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbi(
            Contract.ShaderStage.Compute,
            resources: new[] { fragmentStorage },
            workgroupSize: new Contract.ShaderWorkgroupSize(1, 1, 1)));

        var duplicateA = new Contract.ShaderResourceBinding(
            new Contract.ShaderBinding(0, 0),
            Contract.ShaderResourceKind.StorageBuffer,
            Contract.ShaderResourceAccess.Read,
            Contract.ShaderStageMask.Compute);
        var duplicateB = new Contract.ShaderResourceBinding(
            new Contract.ShaderBinding(0, 0),
            Contract.ShaderResourceKind.SampledTexture,
            Contract.ShaderResourceAccess.Read,
            Contract.ShaderStageMask.Compute);
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbi(
            Contract.ShaderStage.Compute,
            resources: new[] { duplicateA, duplicateB },
            workgroupSize: new Contract.ShaderWorkgroupSize(1, 1, 1)));

        var value = new Contract.ShaderValueType(Contract.ShaderValueKind.FloatingPoint, 32);
        Assert.Throws<ArgumentException>(() => new Contract.ShaderAbi(
            Contract.ShaderStage.Fragment,
            inputs: new[]
            {
                new Contract.ShaderInterfaceVariable(value, 0),
                new Contract.ShaderInterfaceVariable(value, 0)
            }));
    }

    [Fact]
    public void SpecializationConstantRequiresExactDefaultSize()
    {
        var scalar = new Contract.ShaderValueType(Contract.ShaderValueKind.FloatingPoint, 32);
        var vector = new Contract.ShaderValueType(Contract.ShaderValueKind.FloatingPoint, 32, VectorSize: 4);

        Assert.Equal(4, new Contract.ShaderSpecializationConstant(0, scalar, new byte[4]).DefaultValue.Length);
        Assert.Equal(16, new Contract.ShaderSpecializationConstant(1, vector, new byte[16]).DefaultValue.Length);
        Assert.Throws<ArgumentException>(() => new Contract.ShaderSpecializationConstant(2, scalar, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderSpecializationConstant(3, vector, new byte[4]));
    }

    [Fact]
    public void PushConstantRangeRequiresAlignedOffsetAndMatchingLayoutSize()
    {
        var stage = Contract.ShaderStageMask.Compute;
        var layout = new Contract.ShaderAbiLayout(16, 16);

        Assert.Equal(16u, new Contract.ShaderPushConstantRange(0, 16, stage, layout).Size);
        Assert.Throws<ArgumentException>(() => new Contract.ShaderPushConstantRange(2, 16, stage, layout));
        Assert.Throws<ArgumentException>(() => new Contract.ShaderPushConstantRange(0, 32, stage, layout));
    }

    private static Contract.ShaderArtifact Artifact(Contract.ShaderStage stage, Contract.ShaderResourceBinding? resource = null, uint pushSize = 0)
    {
        var pushConstants = pushSize == 0
            ? Array.Empty<Contract.ShaderPushConstantRange>()
            : new[] { new Contract.ShaderPushConstantRange(0, pushSize, Contract.ShaderStageMask.AllGraphics, new Contract.ShaderAbiLayout(pushSize, 16)) };
        var abi = new Contract.ShaderAbi(stage, resource is null ? Array.Empty<Contract.ShaderResourceBinding>() : new[] { resource }, pushConstants, workgroupSize: stage == Contract.ShaderStage.Compute ? new Contract.ShaderWorkgroupSize(1, 1, 1) : default);
        return new Contract.ShaderArtifact(ValidSpirv(), "main", abi);
    }

    private static Contract.ShaderAbi ComputeAbi() => new(
        Contract.ShaderStage.Compute,
        workgroupSize: new Contract.ShaderWorkgroupSize(1, 1, 1));

    private static byte[] ValidSpirv(uint version = 0x00010000, uint bound = 1, uint reserved = 0) => Header(version, bound, reserved);

    private static byte[] Header(uint version = 0x00010000, uint bound = 1, uint reserved = 0) =>
    [
        0x03, 0x02, 0x23, 0x07,
        (byte)version, (byte)(version >> 8), (byte)(version >> 16), (byte)(version >> 24),
        0, 0, 0, 0,
        (byte)bound, (byte)(bound >> 8), (byte)(bound >> 16), (byte)(bound >> 24),
        (byte)reserved, (byte)(reserved >> 8), (byte)(reserved >> 16), (byte)(reserved >> 24)
    ];
}
