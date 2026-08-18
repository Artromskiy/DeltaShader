using DVG.Maths;
using DVG.Shaders.Abstractions;

namespace DVG.Shaders.Compiler.Tests.Fixtures;

public static class DvgMathsSymbolFixtures
{
    public static void FixtureOne(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> a,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> b)
    {
        var left = new float3(a.Load(0u), 1f, 2f);
        var right = new float3(a.Load(0u), 3f, 4f);
        b.Store(0u, maths.dot(left, right));
    }

    public static void FixtureSwizzle(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float4> values,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float2> result,
        uint i)
    {
        var v = values.Load(i);
        var sw = v.xy;
        result.Store(i, new float2(sw.x, sw.y));
    }
}
