using System.Collections;
using System.Collections.Generic;

namespace Delta.Shader;

public abstract class ShaderStorageBuffer
{
    protected ShaderStorageBuffer()
    {
    }

    public virtual uint Length => throw new NotSupportedException(
        "Storage buffer semantics are shader-only. This member is not supported on CPU execution paths.");
}

public abstract class ShaderStorageBuffer<T> : ShaderStorageBuffer, IEnumerable<T> where T : unmanaged
{
    public virtual T this[uint index] => throw new NotSupportedException(
        "Shader-only buffer access path. CPU usage is blocked by design for DeltaShader compute prototype.");

    public IEnumerator<T> GetEnumerator() => throw new NotSupportedException(
        "Shader-only buffer enumeration path. CPU usage is blocked by design for DeltaShader compute prototype.");

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ReadOnlyStorageBuffer<T> : ShaderStorageBuffer<T> where T : unmanaged
{
    public override T this[uint index]
    {
        get => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for DeltaShader compute prototype.");
    }

}

public sealed class ReadWriteStorageBuffer<T> : ShaderStorageBuffer<T> where T : unmanaged
{
    public new T this[uint index]
    {
        get => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for DeltaShader compute prototype.");
        set => throw new NotSupportedException(
            "Shader-only buffer access path. CPU usage is blocked by design for DeltaShader compute prototype.");
    }

}
