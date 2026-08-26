using System.Collections.Generic;
using Delta.Shader;

namespace Delta.Shader.Compiler.IR;

public sealed class ShaderIrModule
{
    public ShaderStage Stage { get; init; } = ShaderStage.Compute;
    public string SourceEntryPointName { get; init; } = string.Empty;
    public string EntryPointName { get; init; } = string.Empty;
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public IReadOnlyList<ShaderIrResource> Resources { get; init; } = [];
    public IReadOnlyList<ShaderIrStruct> Structs { get; init; } = [];
    public IReadOnlyList<string> Requirements { get; init; } = [];
    public IReadOnlyList<string> Instructions { get; init; } = [];
    public string? Body { get; init; }
    public IReadOnlyList<string> HelperFunctions { get; init; } = [];
    public bool UsesBuiltinInvocationId { get; init; }
    public string? InvocationParameterName { get; init; }
    public IReadOnlyList<ShaderIrInterfaceVariable> Inputs { get; init; } = [];
    public IReadOnlyList<ShaderIrVertexInput> VertexInputs { get; init; } = [];
    public IReadOnlyList<ShaderIrVertexBufferBinding> VertexBuffers { get; init; } = [];
    public IReadOnlyList<ShaderIrInterfaceVariable> Outputs { get; init; } = [];
    public IReadOnlyList<ShaderIrPushConstant> PushConstants { get; init; } = [];
}

public sealed class ShaderIrInterfaceVariable
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public string? Builtin { get; init; }
}

public sealed class ShaderIrVertexInput
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

public sealed class ShaderIrVertexBufferBinding
{
    public uint Binding { get; init; }
    public uint Stride { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public IReadOnlyList<ShaderIrVertexInput> Attributes { get; init; } = [];
}

public sealed class ShaderIrPushConstant
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<ShaderIrStructMember> Members { get; init; } = [];
}

public sealed class ShaderIrResource
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public ShaderResourceKind Category { get; init; } = ShaderResourceKind.Unknown;
    public ShaderStage Stage { get; init; }
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public ShaderResourceAccess Access { get; init; } = ShaderResourceAccess.ReadWrite;
    public bool ReadOnly { get; init; }
    public string Layout { get; init; } = ShaderStd430Layout.Standard;
    public ShaderStd430Layout? Std430Layout { get; init; }
    public IReadOnlyList<ShaderIrStructMember> Members { get; init; } = [];
}

public sealed class ShaderIrStruct
{
    public string Name { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<ShaderIrStructMember> Members { get; init; } = [];
}

public sealed class ShaderIrStructMember
{
    public string Name { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public IReadOnlyList<ShaderIrStructMember> Members { get; init; } = [];
}
