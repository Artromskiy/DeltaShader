using Legacy = DeltaShader.Abstractions;
using Final = DeltaShader.Contract;

namespace DeltaShader.Tool;

internal static class ShaderArtifactPublisher
{
    public static Final.ShaderArtifact Create(ReadOnlySpan<byte> spirv, Legacy.ShaderAbiManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new Final.ShaderArtifact(spirv, manifest.EntryPointName, ToAbi(manifest));
    }

    private static Final.ShaderAbi ToAbi(Legacy.ShaderAbiManifest manifest)
        => new(
            ToStage(manifest.Stage),
            manifest.Resources.Select(ToResource).ToArray(),
            manifest.PushConstants.Select(ToPushConstant).ToArray(),
            manifest.Inputs.Select(ToInterface).ToArray(),
            manifest.Outputs.Select(ToInterface).ToArray(),
            manifest.VertexInputs.Select(ToVertexInput).ToArray(),
            manifest.VertexBufferBindings.Select(ToVertexBuffer).ToArray(),
            workgroupSize: ToWorkgroup(manifest),
            requiredCapabilities: Final.ShaderCapabilities.None);

    private static Final.ShaderResourceBinding ToResource(Legacy.ShaderAbiResource resource)
    {
        var kind = resource.Category switch
        {
            "storage-buffer" => Final.ShaderResourceKind.StorageBuffer,
            "sampled-texture" or "sampled-texture-2d" => Final.ShaderResourceKind.SampledTexture,
            "combined-texture-sampler" => Final.ShaderResourceKind.CombinedTextureSampler,
            _ => throw new ArgumentException($"Unsupported shader resource category '{resource.Category}'.", nameof(resource))
        };
        var access = resource.Access == 0
            ? resource.ReadOnly ? Final.ShaderResourceAccess.Read : Final.ShaderResourceAccess.ReadWrite
            : (Final.ShaderResourceAccess)resource.Access;
        var layout = kind is Final.ShaderResourceKind.SampledTexture or Final.ShaderResourceKind.CombinedTextureSampler
            ? Final.ShaderAbiLayout.Empty
            : ToLayout(resource.Size, resource.Alignment, resource.ArrayStride, resource.MatrixStride ?? 0u, resource.Members);
        return new Final.ShaderResourceBinding(
            new Final.ShaderBinding(resource.Set, resource.Binding),
            kind,
            access,
            ToStageMask(resource.Stage),
            layout);
    }

    private static Final.ShaderPushConstantRange ToPushConstant(Legacy.ShaderAbiPushConstant pushConstant)
        => new(0u, pushConstant.Size, ToStageMask(Legacy.ShaderStage.Vertex), ToLayout(pushConstant.Size, pushConstant.Alignment, pushConstant.ArrayStride, 0u, pushConstant.Members));

    private static Final.ShaderInterfaceVariable ToInterface(Legacy.ShaderAbiInterfaceVariable variable)
        => new(ToValueType(variable.GlslType), string.IsNullOrWhiteSpace(variable.Builtin) ? variable.Location : null, ToBuiltin(variable.Builtin));

    private static Final.ShaderVertexInput ToVertexInput(Legacy.ShaderAbiVertexInput input)
        => new(input.Location, input.Binding, input.ByteOffset, ToValueType(input.GlslType), (Final.ShaderVertexInputRate)input.InputRate);

    private static Final.ShaderVertexBufferLayout ToVertexBuffer(Legacy.ShaderAbiVertexBufferBinding buffer)
        => new(buffer.Binding, buffer.Stride, (Final.ShaderVertexInputRate)buffer.InputRate);

    private static Final.ShaderAbiLayout ToLayout(uint size, uint alignment, uint arrayStride, uint matrixStride, IEnumerable<Legacy.ShaderAbiMember> members)
        => new(size, alignment, arrayStride, matrixStride, members.Select(ToMember));

    private static Final.ShaderAbiMember ToMember(Legacy.ShaderAbiMember member)
    {
        var nested = IsStructure(member.GlslType)
            ? ToLayout(member.Size, member.Alignment, member.ArrayStride, member.MatrixStride ?? 0u, member.Members)
            : null;
        return new Final.ShaderAbiMember(ToValueType(member.GlslType), member.Offset, member.Size, member.Alignment, member.ArrayStride, member.MatrixStride ?? 0u, nested);
    }

    private static Final.ShaderValueType ToValueType(string? glslType)
    {
        if (IsStructure(glslType))
        {
            return Final.ShaderValueType.Structure;
        }

        var type = glslType ?? string.Empty;
        return type switch
        {
            "bool" => new(Final.ShaderValueKind.Boolean, 32u),
            "int" => new(Final.ShaderValueKind.SignedInteger, 32u),
            "uint" => new(Final.ShaderValueKind.UnsignedInteger, 32u),
            "float" => new(Final.ShaderValueKind.FloatingPoint, 32u),
            "double" => new(Final.ShaderValueKind.FloatingPoint, 64u),
            "vec2" or "vec3" or "vec4" => new(Final.ShaderValueKind.FloatingPoint, 32u, VectorSize(type)),
            "ivec2" or "ivec3" or "ivec4" => new(Final.ShaderValueKind.SignedInteger, 32u, VectorSize(type)),
            "uvec2" or "uvec3" or "uvec4" => new(Final.ShaderValueKind.UnsignedInteger, 32u, VectorSize(type)),
            "bvec2" or "bvec3" or "bvec4" => new(Final.ShaderValueKind.Boolean, 32u, VectorSize(type)),
            "dvec2" or "dvec3" or "dvec4" => new(Final.ShaderValueKind.FloatingPoint, 64u, VectorSize(type)),
            "mat2" or "mat3" or "mat4" => new(Final.ShaderValueKind.FloatingPoint, 32u, VectorSize(type), VectorSize(type)),
            _ => throw new ArgumentException($"Unsupported GLSL ABI type '{glslType}'.", nameof(glslType))
        };
    }

    private static uint VectorSize(string type) => (uint)(type[type.Length - 1] - '0');

    private static Final.ShaderWorkgroupSize ToWorkgroup(Legacy.ShaderAbiManifest manifest)
        => manifest.Stage == Legacy.ShaderStage.Compute
            ? new Final.ShaderWorkgroupSize(manifest.LocalSizeX, manifest.LocalSizeY, manifest.LocalSizeZ)
            : default;

    private static Final.ShaderStage ToStage(Legacy.ShaderStage stage) => stage switch
    {
        Legacy.ShaderStage.Compute => Final.ShaderStage.Compute,
        Legacy.ShaderStage.Vertex => Final.ShaderStage.Vertex,
        Legacy.ShaderStage.Fragment => Final.ShaderStage.Fragment,
        _ => Final.ShaderStage.Unknown
    };

    private static Final.ShaderStageMask ToStageMask(Legacy.ShaderStage stage) => stage switch
    {
        Legacy.ShaderStage.Compute => Final.ShaderStageMask.Compute,
        Legacy.ShaderStage.Vertex => Final.ShaderStageMask.Vertex,
        Legacy.ShaderStage.Fragment => Final.ShaderStageMask.Fragment,
        _ => Final.ShaderStageMask.None
    };

    private static Final.ShaderBuiltin ToBuiltin(string? builtin) => builtin switch
    {
        "FragmentCoord" => Final.ShaderBuiltin.FragmentCoordinate,
        "Position" => Final.ShaderBuiltin.Position,
        "VertexIndex" => Final.ShaderBuiltin.VertexIndex,
        "InstanceIndex" => Final.ShaderBuiltin.InstanceIndex,
        null or "" or "FragmentColor" => Final.ShaderBuiltin.None,
        _ => Enum.TryParse<Final.ShaderBuiltin>(builtin, out var value) ? value : Final.ShaderBuiltin.Unknown
    };

    private static bool IsStructure(string? glslType)
        => glslType is not null && glslType.Length > 0 && glslType.StartsWith("DeltaStruct_", StringComparison.Ordinal);
}
