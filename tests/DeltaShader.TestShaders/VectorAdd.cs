using Delta.Shader;
using Delta.Maths;

namespace Delta.Shader.TestShaders;

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

    public readonly struct ComputeContext
    {
        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<TransformRecord> Input;

        [Layout(0, 1)]
        public readonly ReadWriteStorageBuffer<TransformRecord> Output;
    }

    [Compute(localSizeX: 8)]
    public static void Compute(in ComputeContext context)
    {
        uint invocation = ShaderBuiltins.GlobalInvocationId.X;
        if (invocation < context.Input.Length)
        {
            context.Output[invocation] = context.Input[invocation];
        }
    }
}
