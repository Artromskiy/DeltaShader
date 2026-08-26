using DeltaShader.Abstractions;
using DeltaMaths;

namespace DeltaShader.TestShaders;

internal static class VectorAdd
{
    public struct TransformBase
    {
        public float3 Position = default;

        public TransformBase()
        {
        }
    }

    public struct TransformRecord
    {
        public TransformBase Base = default;
        public quaternion Rotation = default;
        public float4x4 Transform = default;

        public TransformRecord()
        {
        }
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
