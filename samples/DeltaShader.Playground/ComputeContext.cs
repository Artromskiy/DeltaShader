using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Playground;

public readonly struct ComputeContext
{
    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<uint> Input;

    [Layout(0, 1)]
    public readonly ReadWriteStorageBuffer<uint> Output;

    [PushConstant]
    public readonly float DeltaTime;
}
