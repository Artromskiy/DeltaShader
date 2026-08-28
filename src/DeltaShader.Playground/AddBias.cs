using Delta.Shader;

namespace Delta.Shader.Playground;

public static class AddBiasShader
{
    [ComputeShader(localSizeX: 64)]
    public static void AddBias(in ComputeContext context)
    {
        uint id = ShaderBuiltins.GlobalInvocationId.X;
        if (id < context.Input.Length)
        {
            context.Output[id] = context.Input[id] + 7u;
        }
    }
}
