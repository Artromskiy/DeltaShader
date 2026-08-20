using System;

namespace Delta.Shader.Abstractions;

public abstract class ShaderStorageBuffer
{
    protected ShaderStorageBuffer()
    {
    }

    public virtual uint Length => throw new NotSupportedException(
        "Storage buffer semantics are shader-only. This member is not supported on CPU execution paths.");
}

public abstract class ShaderStorageBuffer<T> : ShaderStorageBuffer where T : unmanaged
{
    public abstract T Load(uint index);
    public virtual void Store(uint index, T value) => throw new NotSupportedException(
        "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
}

public sealed class ReadOnlyStorageBuffer<T> : ShaderStorageBuffer<T> where T : unmanaged
{
    public T this[uint index]
    {
        get => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
    }

    public override T Load(uint index)
    {
        throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
    }
}

public sealed class ReadWriteStorageBuffer<T> : ShaderStorageBuffer<T> where T : unmanaged
{
    public T this[uint index]
    {
        get => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
        set => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
    }

    public override T Load(uint index)
    {
        return this[index];
    }

    public override void Store(uint index, T value)
    {
        this[index] = value;
    }
}
