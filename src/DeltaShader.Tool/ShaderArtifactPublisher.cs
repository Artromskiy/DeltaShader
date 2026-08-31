using System.Globalization;
using Compiler = Delta.Shader.Compiler;
using Final = Delta.Shader.Contract;

namespace Delta.Shader.Tool;

internal static class ShaderArtifactPublisher
{
    public static Final.ShaderArtifact Create(
        ReadOnlySpan<byte> spirv,
        Compiler.ShaderCompilationManifest manifest,
        Final.ShaderCapabilities requiredCapabilities = Final.ShaderCapabilities.None)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new Final.ShaderArtifact(spirv, manifest.EntryPointName, ToAbi(manifest, requiredCapabilities));
    }

    private static Final.ShaderAbi ToAbi(
        Compiler.ShaderCompilationManifest manifest,
        Final.ShaderCapabilities requiredCapabilities)
        => new(
            ToStage(manifest.Stage),
            manifest.Resources.Select(ToResource).ToArray(),
            manifest.PushConstants.Select(push => ToPushConstant(push, manifest.Stage)).ToArray(),
            manifest.Inputs.Select(ToInterface).ToArray(),
            manifest.Outputs.Select(ToInterface).ToArray(),
            manifest.VertexInputs.Select(ToVertexInput).ToArray(),
            manifest.VertexBufferBindings.Select(ToVertexBuffer).ToArray(),
            workgroupSize: ToWorkgroup(manifest),
            requiredCapabilities: requiredCapabilities);

    private static Final.ShaderResourceBinding ToResource(Compiler.ShaderCompilationResource resource)
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

    private static Final.ShaderPushConstantRange ToPushConstant(
        Compiler.ShaderCompilationPushConstant pushConstant,
        Delta.Shader.ShaderStage stage)
        => new(0u, pushConstant.Size, ToStageMask(stage), ToLayout(pushConstant.Size, pushConstant.Alignment, pushConstant.ArrayStride, 0u, pushConstant.Members));

    private static Final.ShaderInterfaceVariable ToInterface(Compiler.ShaderCompilationInterfaceVariable variable)
        => new(ToValueType(variable.GlslType), string.IsNullOrWhiteSpace(variable.Builtin) ? variable.Location : null, ToBuiltin(variable.Builtin));

    private static Final.ShaderVertexInput ToVertexInput(Compiler.ShaderCompilationVertexInput input)
        => new(input.Location, input.Binding, input.ByteOffset, ToValueType(input.GlslType), (Final.ShaderVertexInputRate)input.InputRate);

    private static Final.ShaderVertexBufferLayout ToVertexBuffer(Compiler.ShaderCompilationVertexBufferBinding buffer)
        => new(buffer.Binding, buffer.Stride, (Final.ShaderVertexInputRate)buffer.InputRate);

    private static Final.ShaderAbiLayout ToLayout(uint size, uint alignment, uint arrayStride, uint matrixStride, IEnumerable<Compiler.ShaderCompilationMember> members)
    {
        var memberArray = members.ToArray();
        if (size == 0 && alignment == 0 && arrayStride == 0 && matrixStride == 0 && memberArray.Length == 0)
        {
            return Final.ShaderAbiLayout.Empty;
        }

        var paddedSize = Math.Max(size, alignment);
        return new(paddedSize, alignment, arrayStride, matrixStride, memberArray.Select(ToMember));
    }

    private static Final.ShaderAbiMember ToMember(Compiler.ShaderCompilationMember member)
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
            "float16_t" => new(Final.ShaderValueKind.FloatingPoint, 16u),
            "float" => new(Final.ShaderValueKind.FloatingPoint, 32u),
            "double" => new(Final.ShaderValueKind.FloatingPoint, 64u),
            "f16vec2" or "f16vec3" or "f16vec4" => new(Final.ShaderValueKind.FloatingPoint, 16u, VectorSize(type)),
            "vec2" or "vec3" or "vec4" => new(Final.ShaderValueKind.FloatingPoint, 32u, VectorSize(type)),
            "ivec2" or "ivec3" or "ivec4" => new(Final.ShaderValueKind.SignedInteger, 32u, VectorSize(type)),
            "uvec2" or "uvec3" or "uvec4" => new(Final.ShaderValueKind.UnsignedInteger, 32u, VectorSize(type)),
            "bvec2" or "bvec3" or "bvec4" => new(Final.ShaderValueKind.Boolean, 32u, VectorSize(type)),
            "dvec2" or "dvec3" or "dvec4" => new(Final.ShaderValueKind.FloatingPoint, 64u, VectorSize(type)),
            "mat2" or "mat3" or "mat4"
                or "mat2x3" or "mat2x4" or "mat3x2" or "mat3x4" or "mat4x2" or "mat4x3"
                => MatrixValueType(type, 32u),
            "f16mat2" or "f16mat3" or "f16mat4"
                or "f16mat2x3" or "f16mat2x4" or "f16mat3x2" or "f16mat3x4" or "f16mat4x2" or "f16mat4x3"
                => MatrixValueType(type, 16u),
            "dmat2" or "dmat3" or "dmat4"
                or "dmat2x3" or "dmat2x4" or "dmat3x2" or "dmat3x4" or "dmat4x2" or "dmat4x3"
                => MatrixValueType(type, 64u),
            _ => throw new ArgumentException($"Unsupported GLSL ABI type '{glslType}'.", nameof(glslType))
        };
    }

    private static uint VectorSize(string type) => (uint)(type[type.Length - 1] - '0');

    private static Final.ShaderValueType MatrixValueType(string type, uint bitWidth)
    {
        var dimensions = type[(type.LastIndexOf('t') + 1)..];
        var separator = dimensions.IndexOf('x', StringComparison.Ordinal);
        var columns = separator < 0
            ? VectorSize(dimensions)
            : uint.Parse(dimensions.AsSpan(0, separator), CultureInfo.InvariantCulture);
        var rows = separator < 0
            ? columns
            : uint.Parse(dimensions.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        return new(Final.ShaderValueKind.FloatingPoint, bitWidth, columns, rows);
    }

    private static Final.ShaderWorkgroupSize ToWorkgroup(Compiler.ShaderCompilationManifest manifest)
        => manifest.Stage == Delta.Shader.ShaderStage.Compute
            ? new Final.ShaderWorkgroupSize(manifest.LocalSizeX, manifest.LocalSizeY, manifest.LocalSizeZ)
            : default;

    private static Final.ShaderStage ToStage(Delta.Shader.ShaderStage stage) => stage switch
    {
        Delta.Shader.ShaderStage.Compute => Final.ShaderStage.Compute,
        Delta.Shader.ShaderStage.Vertex => Final.ShaderStage.Vertex,
        Delta.Shader.ShaderStage.Fragment => Final.ShaderStage.Fragment,
        _ => Final.ShaderStage.Unknown
    };

    private static Final.ShaderStageMask ToStageMask(Delta.Shader.ShaderStage stage) => stage switch
    {
        Delta.Shader.ShaderStage.Compute => Final.ShaderStageMask.Compute,
        Delta.Shader.ShaderStage.Vertex => Final.ShaderStageMask.Vertex,
        Delta.Shader.ShaderStage.Fragment => Final.ShaderStageMask.Fragment,
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
