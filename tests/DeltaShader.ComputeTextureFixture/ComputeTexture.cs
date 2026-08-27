using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ComputeTextureFixture;

public static class ComputeTexture
{
    public readonly struct ComputeContext
    {
        [Layout(0, 2)]
        public readonly SampledTexture2D Atlas;

        [Layout(0, 1)]
        public readonly ReadWriteStorageBuffer<float4> Output;
    }

    [ComputeShader(localSizeX: 8)]
    public static void Compute(in ComputeContext context)
    {
        uint id = ShaderBuiltins.GlobalInvocationId.X;
        if (id < context.Output.Length)
        {
            context.Output[id] = ShaderIntrinsics.SampleCompute<float2, float4>(context.Atlas, new float2(0.5f, 0.5f));
        }
    }
}
