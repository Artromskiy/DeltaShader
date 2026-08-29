using Delta.Shader.Packing;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class Std430PackingTests
{
    [Fact]
    public void WriterWritesLittleEndianScalarsAndLeavesPaddingUntouched()
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Fill(0xcc);
        var writer = new Std430Writer(bytes);

        writer.WriteUInt(0u, 0x11223344u);
        writer.WriteFloat(8u, 1.0f);
        writer.WriteBool(12u, true);

        Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11 }, bytes[..4].ToArray());
        Assert.Equal(new byte[] { 0xcc, 0xcc, 0xcc, 0xcc }, bytes[4..8].ToArray());
        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0x3f }, bytes[8..12].ToArray());
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00 }, bytes[12..16].ToArray());
    }

    [Fact]
    public void ArrayByteLengthUsesResolvedStride()
    {
        Assert.Equal(96, Std430Packer.GetArrayByteLength(3, 32u));
        Assert.Throws<ArgumentException>(() => Std430Packer.RequireCapacity(new byte[31], 32u));
    }

    [Fact]
    public void ReaderRoundTripsLittleEndianScalars()
    {
        Span<byte> bytes = stackalloc byte[16];
        var writer = new Std430Writer(bytes);
        writer.WriteUInt(0u, 0x78563412u);
        writer.WriteFloat(4u, 1.5f);
        writer.WriteBool(8u, true);
        writer.WriteBool(12u, false);

        var reader = new Std430Reader(bytes);
        Assert.Equal(0x78563412u, reader.ReadUInt(0u));
        Assert.Equal(1.5f, reader.ReadFloat(4u));
        Assert.True(reader.ReadBool(8u));
        Assert.False(reader.ReadBool(12u));
    }
}
