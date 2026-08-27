using System;

namespace Delta.Shader;

public readonly struct ShaderBuiltinUInt3
{
    public uint X => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
    public uint Y => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
    public uint Z => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
}

public readonly struct ShaderBuiltinFloat4
{
    public float X => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
    public float Y => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
    public float Z => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
    public float W => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
}

public static class ShaderBuiltins
{
    public static ShaderBuiltinUInt3 GlobalInvocationId
        => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");

    public static uint VertexIndex
        => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");

    public static uint InstanceIndex
        => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");

    public static ShaderBuiltinFloat4 FragmentCoord
        => throw new NotSupportedException("Shader builtin values are compiler intrinsics.");
}
