# Compute scenario for 0.1 MVP

> Historical 0.1 sketch. The current contract is `[ComputeShader]` with indexed
> resource views; see `../README.md` and `../WORKFLOW.md`.
>
> Runtime compilation of arbitrary lambdas is intentionally outside this
> slice; the shader source must be a static compile-time method.
>
> The GLSL and manifest expectations below describe compiler/build
> intermediates. A publisher compiles and validates the SPIR-V, resolves the
> concrete ABI, and constructs `DeltaShader.Contract.ShaderArtifact`.
> DeltaRender receives that final artifact, not the GLSL text or compiler
> manifest.

Illustrative historical C# input (use the fixture and `../README.md` for the
current authoring syntax):

```csharp
public readonly struct AddContext
{
    [Layout(0, 0)] public readonly ReadOnlyStorageBuffer<float4> InputA;
    [Layout(0, 1)] public readonly ReadOnlyStorageBuffer<float4> InputB;
    [Layout(0, 2)] public readonly ReadWriteStorageBuffer<float4> Output;
}

[Compute(localSizeX: 32)]
public static void Add(in AddContext context)
{
    uint idx = ShaderBuiltins.GlobalInvocationId.X;
    context.Output[idx] = context.InputA[idx] + context.InputB[idx];
}
```

Expected generated GLSL skeleton:

```glsl
#version 460
layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;
layout(set = 0, binding = 0, std430) readonly buffer InA { vec4 _d0[]; } inputA;
layout(set = 0, binding = 1, std430) readonly buffer InB { vec4 _d1[]; } inputB;
layout(set = 0, binding = 2, std430) writeonly buffer Out { vec4 _d2[]; } output;
void main() { }
```

Compiler-manifest expectation (later materialized as
`ShaderArtifact.EntryPoint` and concrete `ShaderAbi` fields):

- source entry name `Add`, resolved Vulkan/final entry point `main`
- one workgroup size `(32,1,1)`
- 3 storage resources with explicit descriptor sets
- output values check: each component equals sum of corresponding inputs.
