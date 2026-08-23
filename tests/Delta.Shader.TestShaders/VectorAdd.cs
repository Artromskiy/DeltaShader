using Delta.Shader.Abstractions;
using Delta.Maths;

namespace Delta.Shader.TestShaders;

public static class VectorAdd
{
    public struct TransformBase
    {
        public float3 Position;
    }

    public struct TransformRecord
    {
        public TransformBase Base;
        public quaternion Rotation;
        public float4x4 Transform;
    }

    [ComputeShader(localSizeX: 8)]
    public static void Compute(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<TransformRecord> input,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<TransformRecord> output,
        [GlobalInvocationId] uint invocation)
    {
        if (invocation < input.Length)
        {
            output.Store(invocation, input.Load(invocation));
        }
    }
}
