using DeltaMaths;
using DeltaShader.Abstractions;

namespace DeltaShader.Compiler.ReferenceFixtures;

internal static class VectorSymbolFixture
{
    [ComputeShader(localSizeX: 4, localSizeY: 2, localSizeZ: 1)]
    public static void SymbolMapKernel(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float3> input,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float2> output,
        uint invocationIndex)
    {
        var a = input.Load(invocationIndex);
        var b = new float3(1f, 2f, 3f);
        var c = a + b;
        var xy = c.xy;

        output.Store(invocationIndex, new float2(xy.x, xy.y));
        _ = maths.dot(a, b);
        _ = maths.normalize(c);
    }

    public static float FakeDeltaMathsDotLike(float3 left, float3 right)
    {
        return left.x * right.x + left.y * right.y + left.z * right.z;
    }
}
