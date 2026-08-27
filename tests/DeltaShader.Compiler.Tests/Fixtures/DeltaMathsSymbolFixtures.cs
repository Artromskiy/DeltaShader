using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Compiler.Tests.Fixtures;

internal static class DeltaMathsSymbolFixtures
{
    public static void FixtureOne(
        [Layout(0, 0)] ReadOnlyStorageBuffer<float> a,
        [Layout(0, 1)] ReadWriteStorageBuffer<float> b)
    {
        var left = new float3(a[0u], 1f, 2f);
        var right = new float3(a[0u], 3f, 4f);
        b[0u] = maths.dot(left, right);
    }

    public static void FixtureSwizzle(
        [Layout(0, 0)] ReadOnlyStorageBuffer<float4> values,
        [Layout(0, 1)] ReadWriteStorageBuffer<float2> result,
        uint i)
    {
        float4 v = values[i];
        float2 sw = v.xy;
        result[i] = new float2(sw.x, sw.y);
    }
}
