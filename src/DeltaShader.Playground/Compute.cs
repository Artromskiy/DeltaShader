using Delta.Shader;

namespace Delta.Shader.Playground;

public static class PlaygroundShaders
{
    [ComputeShader(localSizeX: 64)]
    public static void SequenceMovement(in ComputeContext context)
    {
        uint index = ShaderBuiltins.GlobalInvocationId.X;
        if (index < context.Input.Length)
        {
            context.Output[index] = context.Output[index] + context.Input[index];
        }
    }
}
