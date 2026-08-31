using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Delta.Shader.Packing;

/// <summary>Validates destination storage used by generated std430 packers.</summary>
public static class Std430Packer
{
    public static void RequireCapacity(Span<ShaderBufferRange> destination, int requiredCount)
    {
        if (destination.Length < requiredCount)
        {
            throw new ArgumentException(
                $"The destination must contain at least {requiredCount} buffer ranges.",
                nameof(destination));
        }
    }

    public static uint AlignOffset(uint offset, uint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        var mask = alignment - 1;
        return checked((offset + mask) & ~mask);
    }

    public static Span<byte> GetRange(Span<byte> backing, ShaderBufferRange range)
    {
        var end = checked(range.Offset + range.Size);
        RequireCapacity(backing, end);
        return backing.Slice(checked((int)range.Offset), checked((int)range.Size));
    }

    public static ReadOnlySpan<byte> GetRange(ReadOnlySpan<byte> backing, ShaderBufferRange range)
    {
        var end = checked(range.Offset + range.Size);
        RequireCapacity(backing, end);
        return backing.Slice(checked((int)range.Offset), checked((int)range.Size));
    }

    public static void RequireCapacity(ReadOnlySpan<byte> destination, uint requiredSize)
    {
        if ((ulong)destination.Length < requiredSize)
        {
            throw new ArgumentException(
                $"The destination must contain at least {requiredSize} bytes.",
                nameof(destination));
        }
    }

    public static int GetArrayByteLength(int elementCount, uint elementStride)
    {
#if NET10_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
#else
        if (elementCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount));
        }
#endif

        var byteLength = (ulong)elementCount * elementStride;
        if (byteLength > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount), "The packed array is too large for a single managed buffer.");
        }

        return (int)byteLength;
    }

    public static int GetBackingByteLength(ReadOnlySpan<ShaderBufferRange> ranges)
    {
        uint end = 0;
        foreach (var range in ranges)
        {
            var rangeEnd = checked(range.Offset + range.Size);
            end = Math.Max(end, rangeEnd);
        }

        if (end > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ranges),
                "The packed ranges are too large for a single managed buffer.");
        }

        return (int)end;
    }
}

public readonly record struct ShaderBufferRange(
    uint Set,
    uint Binding,
    uint Offset,
    uint Size,
    uint ElementStride);

/// <summary>Writes primitive values at offsets resolved by the shader compiler.</summary>
public ref struct Std430Writer
{
    private Span<byte> _destination;

    public Std430Writer(Span<byte> destination)
    {
        _destination = destination;
    }

    public void WriteBool(uint offset, bool value) => WriteUInt(offset, value ? 1u : 0u);

    public void WriteInt(uint offset, int value) => WriteUInt(offset, unchecked((uint)value));

    public void WriteUInt(uint offset, uint value)
    {
        EnsureRange(offset, 4u);
        BinaryPrimitives.WriteUInt32LittleEndian(
            _destination.Slice(checked((int)offset), sizeof(uint)), value);
    }

    public void WriteFloat(uint offset, float value)
        => WriteUInt(offset, Unsafe.As<float, uint>(ref value));

    public void WriteDouble(uint offset, double value)
        => WriteULong(offset, Unsafe.As<double, ulong>(ref value));

    public void WriteHalf(uint offset, ushort value)
    {
        EnsureRange(offset, sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(
            _destination.Slice(checked((int)offset), sizeof(ushort)), value);
    }

    private void WriteULong(uint offset, ulong value)
    {
        EnsureRange(offset, sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(
            _destination.Slice(checked((int)offset), sizeof(ulong)), value);
    }

    private void EnsureRange(uint offset, uint size)
    {
        if ((ulong)offset + size > (ulong)_destination.Length)
        {
            throw new ArgumentException("The write range does not fit the destination.", nameof(offset));
        }
    }
}

/// <summary>Reads primitive values at offsets resolved by the shader compiler.</summary>
public ref struct Std430Reader
{
    private ReadOnlySpan<byte> _source;

    public Std430Reader(ReadOnlySpan<byte> source)
    {
        _source = source;
    }

    public bool ReadBool(uint offset) => ReadUInt(offset) != 0u;

    public int ReadInt(uint offset) => unchecked((int)ReadUInt(offset));

    public uint ReadUInt(uint offset)
    {
        EnsureRange(offset, 4u);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _source.Slice(checked((int)offset), sizeof(uint)));
    }

    public float ReadFloat(uint offset)
    {
        var bits = ReadUInt(offset);
        return Unsafe.As<uint, float>(ref bits);
    }

    public double ReadDouble(uint offset)
    {
        var bits = ReadULong(offset);
        return Unsafe.As<ulong, double>(ref bits);
    }

    public ushort ReadHalf(uint offset)
    {
        EnsureRange(offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(
            _source.Slice(checked((int)offset), sizeof(ushort)));
    }

    private ulong ReadULong(uint offset)
    {
        EnsureRange(offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(
            _source.Slice(checked((int)offset), sizeof(ulong)));
    }

    private void EnsureRange(uint offset, uint size)
    {
        if ((ulong)offset + size > (ulong)_source.Length)
        {
            throw new ArgumentException("The read range does not fit the source.", nameof(offset));
        }
    }
}
