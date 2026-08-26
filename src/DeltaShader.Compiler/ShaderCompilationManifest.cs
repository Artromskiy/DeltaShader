using Delta.Shader;

namespace Delta.Shader.Compiler;

/// <summary>Build-time compiler metadata. It is not part of the runtime artifact contract.</summary>
public sealed class ShaderCompilationManifest
{
    public ShaderStage Stage { get; init; } = ShaderStage.Compute;
    public string SourceEntryPointName { get; init; } = string.Empty;
    public string EntryPointName { get; init; } = string.Empty;
    public string TargetProfile { get; init; } = "vulkan1.2";
    public string GlslVersion { get; init; } = "460";
    public string SpirvVersion { get; init; } = "1.5";
    public string StorageLayout { get; init; } = "std430";
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public IReadOnlyList<ShaderCompilationResource> Resources { get; init; } = Array.Empty<ShaderCompilationResource>();
    public IReadOnlyList<ShaderCompilationInterfaceVariable> Inputs { get; init; } = Array.Empty<ShaderCompilationInterfaceVariable>();
    public IReadOnlyList<ShaderCompilationVertexInput> VertexInputs { get; init; } = Array.Empty<ShaderCompilationVertexInput>();
    public IReadOnlyList<ShaderCompilationVertexBufferBinding> VertexBufferBindings { get; init; } = Array.Empty<ShaderCompilationVertexBufferBinding>();
    public IReadOnlyList<ShaderCompilationInterfaceVariable> Outputs { get; init; } = Array.Empty<ShaderCompilationInterfaceVariable>();
    public IReadOnlyList<ShaderCompilationPushConstant> PushConstants { get; init; } = Array.Empty<ShaderCompilationPushConstant>();
}

public sealed class ShaderCompilationInterfaceVariable
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public string? Builtin { get; init; }
}

public sealed class ShaderCompilationVertexInput
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public uint Binding { get; init; }
    public uint ByteOffset { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public uint ByteSize { get; init; }
    public uint Alignment { get; init; }
    public string FormatHint { get; init; } = string.Empty;
}

public sealed class ShaderCompilationVertexBufferBinding
{
    public uint Binding { get; init; }
    public uint Stride { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public IReadOnlyList<ShaderCompilationVertexInput> Attributes { get; init; } = Array.Empty<ShaderCompilationVertexInput>();
}

public sealed class ShaderCompilationPushConstant
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<ShaderCompilationMember> Members { get; init; } = Array.Empty<ShaderCompilationMember>();
}

public sealed class ShaderCompilationResource
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public ShaderStage Stage { get; init; }
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public bool ReadOnly { get; init; }
    public ShaderResourceAccess Access { get; init; } = ShaderResourceAccess.ReadWrite;
    public string Layout { get; init; } = "std430";
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public IReadOnlyList<ShaderCompilationMember> Members { get; init; } = Array.Empty<ShaderCompilationMember>();
    public ShaderCompilationPackingPlan Packing { get; init; } = new();
}

public sealed class ShaderCompilationPackingPlan
{
    public string Scheme { get; init; } = "std430";
    public string Strategy { get; init; } = "std430-explicit-members";
    public bool DirectRawUploadAllowed { get; init; }
    public string BoolRepresentation { get; init; } = "uint32";
    public uint Stride { get; init; }
}

public sealed class ShaderCompilationMember
{
    public string Name { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public string HostRepresentation { get; init; } = "std430";
    public IReadOnlyList<ShaderCompilationMember> Members { get; init; } = Array.Empty<ShaderCompilationMember>();
}
