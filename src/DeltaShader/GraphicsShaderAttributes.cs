namespace Delta.Shader;

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

public enum VertexInputRate
{
    Vertex = 0,
    Instance = 1
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

    [ShaderIntrinsic("dFdx", ShaderStage.Fragment)]
    public static T dFdx<T>(T value) => throw new NotSupportedException();

    [ShaderIntrinsic("dFdy", ShaderStage.Fragment)]
    public static T dFdy<T>(T value) => throw new NotSupportedException();

    [ShaderIntrinsic("texture", ShaderStage.Vertex)]
    public static TColor SampleVertex<TCoordinate, TColor>(SampledTexture2D texture, TCoordinate coordinate)
        => throw new NotSupportedException();

    [ShaderIntrinsic("texture", ShaderStage.Fragment)]
    public static TColor SampleFragment<TCoordinate, TColor>(SampledTexture2D texture, TCoordinate coordinate)
        => throw new NotSupportedException();

    [ShaderIntrinsic("texture", ShaderStage.Compute)]
    public static TColor SampleCompute<TCoordinate, TColor>(SampledTexture2D texture, TCoordinate coordinate)
        => throw new NotSupportedException();
}
