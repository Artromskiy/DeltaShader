using System;
using System.Runtime.CompilerServices;

namespace Delta.Shader.Packing;

/// <summary>Validates destination storage used by generated std430 packers.</summary>
public static class Std430Packer
{
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
}

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
        var index = (int)offset;
        _destination[index] = (byte)value;
        _destination[index + 1] = (byte)(value >> 8);
        _destination[index + 2] = (byte)(value >> 16);
        _destination[index + 3] = (byte)(value >> 24);
    }

    public void WriteFloat(uint offset, float value)
        => WriteUInt(offset, Unsafe.As<float, uint>(ref value));

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
        var index = (int)offset;
        return (uint)(_source[index] |
            (_source[index + 1] << 8) |
            (_source[index + 2] << 16) |
            (_source[index + 3] << 24));
    }

    public float ReadFloat(uint offset)
    {
        var bits = ReadUInt(offset);
        return Unsafe.As<uint, float>(ref bits);
    }

    private void EnsureRange(uint offset, uint size)
    {
        if ((ulong)offset + size > (ulong)_source.Length)
        {
            throw new ArgumentException("The read range does not fit the source.", nameof(offset));
        }
    }
}
