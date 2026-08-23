namespace Delta.Shader.Abstractions;

public abstract class ShaderStorageBuffer
{
    protected ShaderStorageBuffer()
    {
    }

    public virtual uint Length => throw new NotSupportedException(
        "Storage buffer semantics are shader-only. This member is not supported on CPU execution paths.");
}

public sealed class ReadOnlyStorageBuffer : ShaderStorageBuffer
{
    public uint this[uint index] => throw new NotSupportedException(
        "Shader-only buffer access path. CPU usage is blocked by design.");

    public uint Load(uint index) => this[index];
}

public sealed class ReadWriteStorageBuffer : ShaderStorageBuffer
{
    public uint this[uint index]
    {
        get => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design.");
        set => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design.");
    }

    public uint Load(uint index) => this[index];

    public void Store(uint index, uint value) => this[index] = value;
}

public abstract class ShaderStorageBuffer<T> : ShaderStorageBuffer where T : unmanaged
{
    public virtual T this[uint index] => throw new NotSupportedException(
        "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
    public abstract T Load(uint index);
    public virtual void Store(uint index, T value) => throw new NotSupportedException(
        "Shader-only buffer access path. CPU usage is blocked by design for Delta.Shader compute prototype.");
}

public sealed class ReadOnlyStorageBuffer<T> : ShaderStorageBuffer<T> where T : unmanaged
{
    public override T this[uint index]
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
    public new T this[uint index]
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
