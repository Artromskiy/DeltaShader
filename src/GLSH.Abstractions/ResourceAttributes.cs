using System;

namespace DVG.Shaders.Abstractions;

public enum ShaderResourceAccess
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ShaderResourceAttribute : Attribute
{
    public uint Set { get; }
    public uint Binding { get; }
    public ShaderResourceAccess Access { get; }

    public ShaderResourceAttribute(uint set, uint binding, ShaderResourceAccess access)
    {
        Set = set;
        Binding = binding;
        Access = access;
    }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ReadOnlyStorageBufferAttribute : Attribute
{
    public uint Set { get; }
    public uint Binding { get; }

    public ReadOnlyStorageBufferAttribute(uint set, uint binding)
    {
        Set = set;
        Binding = binding;
    }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ReadWriteStorageBufferAttribute : Attribute
{
    public uint Set { get; }
    public uint Binding { get; }

    public ReadWriteStorageBufferAttribute(uint set, uint binding)
    {
        Set = set;
        Binding = binding;
    }
}

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
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

