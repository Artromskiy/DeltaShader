# DeltaShader

Roslyn-based compiler for a validated C# shader subset targeting Vulkan. It
emits readable GLSL 460, validated SPIR-V and a versioned runtime-neutral ABI
manifest for compute, vertex and fragment stages.

```text
C# project -> Roslyn symbols/IOperation -> typed IR -> GLSL 460
  -> glslangValidator -> SPIR-V -> spirv-val -> ShaderArtifact
```

`Delta.Shader.Abstractions` owns shader attributes, resource wrappers,
`ShaderArtifact`, graphics-program composition, ABI metadata and neutral
compute-dispatch contracts. It depends on neither Roslyn nor Vulkan.
DeltaRender consumes this contract and must not define another manifest or
graphics-program type.

Storage and shared structures use std430. The manifest is authoritative for
offset, alignment, size, array stride and matrix stride. Storage `bool` is four
bytes; CLR `float3[]` must be packed according to the manifest rather than
uploaded as a tightly packed GLSL `vec3[]`.

## Authoring contract

Shaders are compile-time static methods in a small authoring project that
references only abstractions and Maths:

```csharp
[DeltaCompute(localSizeX: 64)]
public static void Compute(
    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<uint> input,
    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<uint> output,
    [GlobalInvocationId] uint id)
{
    if (id < input.Length)
        output[id] = input[id] * 2u + 1u;
}
```

Supported code includes scalar/vector arithmetic, locals, conditionals,
structured loops, static helpers, std430 buffers, push/specialization constants,
stage builtins and supported `Delta.Maths` symbols. Reference types, managed
captures, allocation, exceptions, async, reflection, dynamic, recursion and
virtual/interface dispatch are analyzer errors.

Runtime values are explicit resources or constants, not implicit closure
captures. The source generator emits typed artifact wrappers; the CLI publishes
`.glsl`, `.spv` and `.shader.json` files so consumers need no Roslyn/MSBuild.

Maths mapping comes only from its generated `shader-contract.json` and Roslyn
symbol identity. Source entry names stay in metadata while Vulkan entry points
are currently emitted as `main`.

See [WORKFLOW.md](WORKFLOW.md) for CLI/build/validation commands,
[docs/diagnostics.md](docs/diagnostics.md) for diagnostics,
[TRANSFORM_CONFORMANCE.md](TRANSFORM_CONFORMANCE.md) for CPU/GPU transforms,
and [TODO.md](TODO.md) for selected work. Start agent work at
[AGENTS.md](AGENTS.md).
