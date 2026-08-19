using Delta.Shader.Abstractions;
using Delta.Maths;

namespace Delta.Shader.TestShaders;

public static class VectorAdd
{
    [ComputeShader(localSizeX: 8)]
    public static void Compute(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> output,
        [GlobalInvocationId] uint invocation)
    {
        if (invocation < input.Length)
            output.Store(invocation, input.Load(invocation) * 2f + 1f);
    }
}
