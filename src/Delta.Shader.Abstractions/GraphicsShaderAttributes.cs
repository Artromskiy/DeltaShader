using System;

namespace Delta.Shader.Abstractions;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class VertexShaderAttribute : Attribute
{
    public string? EntryPointName { get; }

    public VertexShaderAttribute(string? entryPointName = null)
    {
        EntryPointName = entryPointName;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class FragmentShaderAttribute : Attribute
{
    public string? EntryPointName { get; }

    public FragmentShaderAttribute(string? entryPointName = null)
    {
        EntryPointName = entryPointName;
    }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class VertexIndexAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class FragmentCoordAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class PositionAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class FragmentColorAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ShaderVaryingAttribute : Attribute
{
    public uint Location { get; }

    public ShaderVaryingAttribute(uint location)
    {
        Location = location;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ShaderIntrinsicAttribute : Attribute
{
    public string GlslName { get; }
    public ShaderStage Stage { get; }

    public ShaderIntrinsicAttribute(string glslName, ShaderStage stage)
    {
        GlslName = glslName;
        Stage = stage;
    }
}

public static class ShaderIntrinsics
{
    [ShaderIntrinsic("fwidth", ShaderStage.Fragment)]
    public static float fwidth(float value) => throw new NotSupportedException();

    [ShaderIntrinsic("smoothstep", ShaderStage.Fragment)]
    public static float smoothstep(float edge0, float edge1, float value) => throw new NotSupportedException();
}
