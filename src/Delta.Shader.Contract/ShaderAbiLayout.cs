using System.Collections.ObjectModel;

namespace Delta.Shader.Contract;

public sealed class ShaderAbiLayout
{
    private readonly ReadOnlyCollection<ShaderAbiMember> _members;

    public ShaderAbiLayout(
        uint size,
        uint alignment,
        uint arrayStride = 0,
        uint matrixStride = 0,
        IEnumerable<ShaderAbiMember>? members = null)
    {
        Size = size;
        Alignment = alignment;
        ArrayStride = arrayStride;
        MatrixStride = matrixStride;
        _members = Array.AsReadOnly(members?.ToArray() ?? []);
    }

    public static ShaderAbiLayout Empty { get; } = new(0, 0);

    public uint Size { get; }

    public uint Alignment { get; }

    public uint ArrayStride { get; }

    public uint MatrixStride { get; }

    public IReadOnlyList<ShaderAbiMember> Members => _members;
}

public sealed class ShaderAbiMember
{
    public ShaderAbiMember(
        ShaderValueType type,
        uint offset,
        uint size,
        uint alignment,
        uint arrayStride = 0,
        uint matrixStride = 0,
        ShaderAbiLayout? nestedLayout = null)
    {
        if (!type.IsValid)
        {
            throw new ArgumentException("Shader ABI member type is invalid.", nameof(type));
        }

        if (type.Kind == ShaderValueKind.Structure && nestedLayout is null)
        {
            throw new ArgumentException("Structured ABI members require a nested layout.", nameof(nestedLayout));
        }

        if (type.Kind != ShaderValueKind.Structure && nestedLayout is not null)
        {
            throw new ArgumentException("Only structured ABI members may have a nested layout.", nameof(nestedLayout));
        }

        Type = type;
        Offset = offset;
        Size = size;
        Alignment = alignment;
        ArrayStride = arrayStride;
        MatrixStride = matrixStride;
        NestedLayout = nestedLayout;
    }

    public ShaderValueType Type { get; }

    public uint Offset { get; }

    public uint Size { get; }

    public uint Alignment { get; }

    public uint ArrayStride { get; }

    public uint MatrixStride { get; }

    public ShaderAbiLayout? NestedLayout { get; }
}
