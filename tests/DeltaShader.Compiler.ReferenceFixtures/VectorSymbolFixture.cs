using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Compiler.ReferenceFixtures;

internal static class VectorSymbolFixture
{
    public readonly struct ComputeContext
    {
        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<float3> Input;

        [Layout(0, 1)]
        public readonly ReadWriteStorageBuffer<float2> Output;
    }

    [Compute(localSizeX: 4, localSizeY: 2, localSizeZ: 1)]
    public static void SymbolMapKernel(in ComputeContext context)
    {
        uint invocationIndex = ShaderBuiltins.GlobalInvocationId.X;
        var a = context.Input[invocationIndex];
        var b = new float3(1f, 2f, 3f);
        var c = a + b;
        var xy = c.xy;

        context.Output[invocationIndex] = new float2(xy.x, xy.y);
        _ = maths.dot(a, b);
        _ = maths.normalize(c);
    }

    public static float FakeDeltaMathsDotLike(float3 left, float3 right)
    {
        return left.x * right.x + left.y * right.y + left.z * right.z;
    }
}
