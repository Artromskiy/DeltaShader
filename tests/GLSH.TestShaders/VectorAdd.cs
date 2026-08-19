using Delta.Shader.Abstractions;
using Delta.Maths;

namespace Delta.Shader.TestShaders;

public static class VectorAdd
{
    public const uint ElementCount = 16u;

    [ComputeShader(localSizeX: 8)]
    public static void Compute(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> output,
        [GlobalInvocationId] uint invocation)
    {
        if (invocation < ElementCount)
        {
            output.Store(invocation, input.Load(invocation) * 2f + 1f);
        }
    }
}
