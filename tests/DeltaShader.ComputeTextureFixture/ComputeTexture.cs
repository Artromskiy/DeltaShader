using DeltaMaths;
using DeltaShader.Abstractions;

namespace DeltaShader.ComputeTextureFixture;

public static class ComputeTexture
{
    [DeltaCompute(localSizeX: 8)]
#pragma warning disable CA1062 // Shader resource parameters are validated by the compiler and are not CLR runtime inputs.
    public static void Compute(
        [SampledTexture2D(0, 2, ShaderStageMask.Compute)] SampledTexture2D atlas,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float4> output,
        [GlobalInvocationId] uint id)
    {
        if (id < output.Length)
        {
            output[id] = ShaderIntrinsics.SampleCompute<float2, float4>(atlas, new float2(0.5f, 0.5f));
        }
    }
#pragma warning restore CA1062
}
