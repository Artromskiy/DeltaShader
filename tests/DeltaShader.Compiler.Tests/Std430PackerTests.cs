using Delta.Shader.Packing;
using Xunit;

namespace Delta.Shader.Compiler.Tests;

public sealed class Std430PackerTests
{
    [Theory]
    [InlineData(0, 16u, 0)]
    [InlineData(1, 16u, 16)]
    [InlineData(3, 32u, 96)]
    public void ArrayByteLength_UsesElementStride(int count, uint stride, int expected)
    {
        Assert.Equal(expected, Std430Packer.GetArrayByteLength(count, stride));
    }

    [Theory]
    [InlineData(0u, 16u, 0u)]
    [InlineData(1u, 16u, 16u)]
    [InlineData(16u, 16u, 16u)]
    [InlineData(17u, 16u, 32u)]
    [InlineData(17u, 32u, 32u)]
    public void AlignOffset_UsesPowerOfTwoAlignment(uint offset, uint alignment, uint expected)
    {
        Assert.Equal(expected, Std430Packer.AlignOffset(offset, alignment));
    }

    [Fact]
    public void BufferRange_PreservesResolvedBindingAndStride()
    {
        var range = new ShaderBufferRange(0u, 3u, 32u, 96u, 32u);

        Assert.Equal(0u, range.Set);
        Assert.Equal(3u, range.Binding);
        Assert.Equal(32u, range.Offset);
        Assert.Equal(96u, range.Size);
        Assert.Equal(32u, range.ElementStride);
    }

    [Fact]
    public void GetRange_ReturnsOnlyTheResolvedBackingSlice()
    {
        var backing = new byte[64];
        var range = new ShaderBufferRange(0u, 2u, 16u, 24u, 8u);

        Span<byte> slice = Std430Packer.GetRange(backing.AsSpan(), range);

        Assert.Equal(24, slice.Length);
        slice[0] = 7;
        Assert.Equal(7, backing[16]);
    }

    [Fact]
    public void BackingByteLength_UsesTheFarthestResolvedRange()
    {
        var ranges = new[]
        {
            new ShaderBufferRange(0u, 0u, 0u, 16u, 16u),
            new ShaderBufferRange(0u, 1u, 32u, 40u, 8u)
        };

        Assert.Equal(72, Std430Packer.GetBackingByteLength(ranges));
    }

    [Fact]
    public void BackingByteLength_ReturnsZeroForEmptyPlan()
    {
        Assert.Equal(0, Std430Packer.GetBackingByteLength(Array.Empty<ShaderBufferRange>()));
    }
}
