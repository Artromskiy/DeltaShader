using Delta.Maths;
using Delta.Shader.Abstractions;

namespace Delta.Shader.Compiler.Tests.Fixtures;

public static class DeltaMathsSymbolFixtures
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
        float4 v = values.Load(i);
        float2 sw = v.xy;
        result.Store(i, new float2(sw.x, sw.y));
    }
}
