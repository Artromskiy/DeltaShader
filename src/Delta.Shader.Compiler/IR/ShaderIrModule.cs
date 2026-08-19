using System.Collections.Generic;

namespace Delta.Shader.Compiler.IR;

public sealed class ShaderIrModule
{
    public string EntryPointName { get; init; } = string.Empty;
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public IReadOnlyList<ShaderIrResource> Resources { get; init; } = [];
    public IReadOnlyList<ShaderIrStruct> Structs { get; init; } = [];
    public IReadOnlyList<string> Requirements { get; init; } = [];
    public IReadOnlyList<string> Instructions { get; init; } = [];
    public string? Body { get; init; }
    public bool UsesBuiltinInvocationId { get; init; }
    public string? InvocationParameterName { get; init; }
}

public sealed class ShaderIrResource
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public bool ReadOnly { get; init; }
    public ShaderStd430Layout? Layout { get; init; }
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
