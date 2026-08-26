namespace Delta.Shader;

[Flags]
public enum ShaderStageMask
{
    None = 0,
    Compute = 1,
    Vertex = 2,
    Fragment = 4,
    Graphics = Vertex | Fragment,
    All = Compute | Graphics
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class SampledTexture2DAttribute : Attribute
{
    public SampledTexture2DAttribute(uint set, uint binding, ShaderStageMask stages = ShaderStageMask.Graphics)
    {
        Set = set;
        Binding = binding;
        Stages = stages;
    }

    public uint Set { get; }
    public uint Binding { get; }
    public ShaderStageMask Stages { get; }
}

/// <summary>Opaque combined image sampler supplied by the graphics runtime.</summary>
public sealed class SampledTexture2D
{
    private SampledTexture2D()
    {
    }
}
