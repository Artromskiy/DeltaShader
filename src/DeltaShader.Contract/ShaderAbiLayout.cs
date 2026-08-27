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
        var memberArray = members?.ToArray() ?? [];
        ValidateLayout(size, alignment, arrayStride, matrixStride, memberArray);
        Size = size;
        Alignment = alignment;
        ArrayStride = arrayStride;
        MatrixStride = matrixStride;
        _members = Array.AsReadOnly(memberArray);
    }

    public static ShaderAbiLayout Empty { get; } = new(0, 0);

    public uint Size { get; }

    public uint Alignment { get; }

    public uint ArrayStride { get; }

    public uint MatrixStride { get; }

    public IReadOnlyList<ShaderAbiMember> Members => _members;

    private static void ValidateLayout(
        uint size,
        uint alignment,
        uint arrayStride,
        uint matrixStride,
        ShaderAbiMember[] members)
    {
        if (size == 0 && alignment == 0 && arrayStride == 0 && matrixStride == 0 && members.Length == 0)
        {
            return;
        }

        if (size == 0 || alignment == 0 || !IsPowerOfTwo(alignment) || size < alignment)
        {
            throw new ArgumentException("A non-empty ABI layout requires a positive power-of-two alignment and size.");
        }

        ValidateStride(arrayStride, size, alignment, "array stride");
        if (matrixStride != 0 && (matrixStride < 4 || matrixStride % 4 != 0))
        {
            throw new ArgumentException("Matrix stride must be a positive multiple of four.", nameof(matrixStride));
        }

        if (members.Any(member => member is null))
        {
            throw new ArgumentException("ABI layouts cannot contain null members.", nameof(members));
        }

        var ordered = members.OrderBy(member => member.Offset).ToArray();
        ulong previousEnd = 0;
        foreach (var member in ordered)
        {
            if (member.Size == 0 || member.Alignment == 0 || !IsPowerOfTwo(member.Alignment) || member.Offset % member.Alignment != 0)
            {
                throw new ArgumentException("ABI members require positive power-of-two alignment and aligned offsets.", nameof(members));
            }

            var memberEnd = (ulong)member.Offset + member.Size;
            if (memberEnd > size || member.Offset < previousEnd)
            {
                throw new ArgumentException("ABI members must be non-overlapping and fit within the containing layout.", nameof(members));
            }

            ValidateStride(member.ArrayStride, member.Size, member.Alignment, "member array stride");
            if (member.MatrixStride != 0 && (member.MatrixStride < 4 || member.MatrixStride % 4 != 0))
            {
                throw new ArgumentException("Member matrix stride must be a positive multiple of four.", nameof(members));
            }

            if (member.NestedLayout is not null && member.NestedLayout.Size > member.Size)
            {
                throw new ArgumentException("Nested ABI layouts cannot exceed their containing member.", nameof(members));
            }

            previousEnd = memberEnd;
        }
    }

    private static void ValidateStride(uint stride, uint size, uint alignment, string name)
    {
        if (stride != 0 && (stride < size || stride % alignment != 0))
        {
            throw new ArgumentException($"{name} must be zero or a multiple of the ABI alignment and member size.", name);
        }
    }

    private static bool IsPowerOfTwo(uint value) => (value & (value - 1)) == 0;
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

        if (size == 0 || alignment == 0 || !IsPowerOfTwo(alignment) || offset % alignment != 0)
        {
            throw new ArgumentException("ABI members require a positive power-of-two alignment and aligned offsets.", nameof(alignment));
        }

        if (arrayStride != 0 && (arrayStride < size || arrayStride % alignment != 0))
        {
            throw new ArgumentException("Member array stride must be zero or a multiple of the ABI alignment and member size.", nameof(arrayStride));
        }

        if (matrixStride != 0 && (matrixStride < 4 || matrixStride % 4 != 0))
        {
            throw new ArgumentException("Member matrix stride must be a positive multiple of four.", nameof(matrixStride));
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

    private static bool IsPowerOfTwo(uint value) => (value & (value - 1)) == 0;
}
