# DeltaShader user API

This is the user-facing C# shader authoring API. It does not define the
cross-project runtime artifact contract; see [CONTRACT.md](CONTRACT.md).

## Authoring

An authoring project references:

- `src/DeltaShader/DeltaShader.csproj` for shader
  attributes, builtins and resource declarations;
- `src/DeltaShader.Analyzers/DeltaShader.Analyzers.csproj` as an analyzer;
- `DeltaMaths` when using supported DeltaMaths shader symbols.

Production authoring is a static attributed method:

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

The analyzer rejects reference types, managed state, captures, allocation,
reflection, virtual/interface dispatch and other unsupported CLR constructs.
Arbitrary runtime lambdas, delegates and expression-tree transpilation are not
part of this API. The CLR method is authoring input; it is never invoked by
the GPU.

## Build-side publication

The CLI emits GLSL and validates SPIR-V through the pinned target profile:

```bash
dotnet run --project src/DeltaShader.Tool/DeltaShader.Tool.csproj \
  -c Release -- build tests/DeltaShader.TestShaders/DeltaShader.TestShaders.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out ./artifacts/shaders
```

The command requires `glslangValidator` and `spirv-val` for the SPIR-V backend.
Runtime consumption, resource creation, descriptor binding, and dispatch are
defined by the producer-owned [CONTRACT.md](CONTRACT.md), not by this authoring
API.
