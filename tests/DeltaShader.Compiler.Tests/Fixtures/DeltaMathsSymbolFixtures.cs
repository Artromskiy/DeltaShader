using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Compiler.Tests.Fixtures;

internal static class DeltaMathsSymbolFixtures
{
    public readonly struct FixtureOneContext
    {
        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<float> A;

        [Layout(0, 1)]
        public readonly ReadWriteStorageBuffer<float> B;
    }

    public static void FixtureOne(in FixtureOneContext context)
    {
        var left = new float3(context.A[0u], 1f, 2f);
        var right = new float3(context.A[0u], 3f, 4f);
        context.B[0u] = maths.dot(left, right);
    }

    public readonly struct FixtureSwizzleContext
    {
        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<float4> Values;

        [Layout(0, 1)]
        public readonly ReadWriteStorageBuffer<float2> Result;

        [PushConstant]
        public readonly uint Index;
    }

    public static void FixtureSwizzle(in FixtureSwizzleContext context)
    {
        float4 v = context.Values[context.Index];
        float2 sw = v.xy;
        context.Result[context.Index] = new float2(sw.x, sw.y);
    }
}
