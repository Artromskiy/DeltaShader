namespace Delta.Shader;

public enum ShaderResourceAccess
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
}

public enum ShaderBindingKind
{
    Descriptor,
    VertexInput,
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class LayoutAttribute : Attribute
{
    public LayoutAttribute(uint location)
    {
        Kind = ShaderBindingKind.VertexInput;
        Location = location;
    }

    public LayoutAttribute(uint set, uint binding)
    {
        Kind = ShaderBindingKind.Descriptor;
        Set = set;
        Binding = binding;
    }

    public ShaderBindingKind Kind { get; }
    public uint Set { get; }
    public uint Binding { get; }
    public uint Location { get; }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class PushConstantAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class SpecializationConstantAttribute : Attribute
{
    public uint Id { get; }

    public SpecializationConstantAttribute(uint id)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Specialization constant IDs must be positive.");
        }

        Id = id;
    }
}
