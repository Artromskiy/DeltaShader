# Compute scenario for 0.1 MVP

Input C# shader (`tests/Delta.Shader.TestShaders/VectorAdd.cs`):

```csharp
[ComputeShader(localSizeX: 32)]
public static void Add(
    ReadOnlyStorageBuffer<float4> inputA,
    ReadOnlyStorageBuffer<float4> inputB,
    ReadWriteStorageBuffer<float4> output,
    uint3 invocation)
{
    var idx = (uint)invocation.x;
    output[idx] = inputA.Load(idx) + inputB.Load(idx);
}
```

Expected generated GLSL skeleton:

```glsl
#version 460
layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;
layout(set = 0, binding = 0, std430) readonly buffer InA { vec4 _d0[]; } inputA;
layout(set = 0, binding = 1, std430) readonly buffer InB { vec4 _d1[]; } inputB;
layout(set = 0, binding = 2, std430) writeonly buffer Out { vec4 _d2[]; } output;
void Add() { }
```

Manifest expectation:
- one `compute` entry point named `Add`
- one workgroup size `(32,1,1)`
- 3 storage resources with explicit descriptor sets
- output values check: each component equals sum of corresponding inputs.
