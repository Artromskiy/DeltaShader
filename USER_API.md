# DeltaShader user API

This is the user-facing API document. Compiler internals, Roslyn symbols,
typed IR, GLSL text and JSON sidecars are not runtime API.

## Authoring

An authoring project references:

- `src/DeltaShader.Abstractions/DeltaShader.Abstractions.csproj` for shader
  attributes, builtins and resource declarations;
- `src/DeltaShader.Analyzers/DeltaShader.Analyzers.csproj` as an analyzer;
- `src/DeltaShader.Contract/DeltaShader.Contract.csproj` when consuming a
  generated final artifact from C# code;
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

## Final runtime artifact

The only runtime handoff is `DeltaShader.Contract.IShaderArtifact`:

```csharp
public interface IShaderArtifact
{
    int FormatVersion { get; }
    ShaderStage Stage { get; }
    string EntryPoint { get; }
    ReadOnlySpan<byte> Spirv { get; }
    ShaderAbi Abi { get; }
}
```

`ShaderArtifact` owns a private copy of SPIR-V. `Spirv` is read-only; consumers
request the explicit upload copy with `CopySpirv()`. `ShaderAbi` is resolved
binary metadata: resource set/binding/kind/access/stages, concrete std430
layout, push constants, stage interfaces, vertex input layouts and compute
workgroup size. It contains no Roslyn state, `System.Type`, generic resource,
delegate, reflection object or GLSL source.

`IGraphicsShaderProgram` contains one validated vertex artifact and one
validated fragment artifact. Its constructor rejects wrong stages and
incompatible shared resources, push constants or stage interfaces.

Generated factories accept SPIR-V bytes and construct this final contract:

```csharp
ShaderArtifact artifact = GeneratedKernelComputeShaderArtifact
    .CreateArtifact(compiledSpirv);

IGraphicsShaderProgram program =
    SdfTextGraphicsShaderProgram.CreateProgram(vertexSpirv, fragmentSpirv);
```

The generated `ManifestJson`/GLSL values and CLI `.shader.json`/`.glsl` files
are inspection and packaging sidecars. Consumers bind resources from
`artifact.Abi`; they do not deserialize a second manifest or recreate the ABI.

## Build-side publication

The CLI emits GLSL and validates SPIR-V through the pinned target profile:

```bash
dotnet run --project src/DeltaShader.Tool/DeltaShader.Tool.csproj \
  -c Release -- build tests/DeltaShader.TestShaders/DeltaShader.TestShaders.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out ./artifacts/shaders
```

The command requires `glslangValidator` and `spirv-val` for the SPIR-V backend.
DeltaRender owns Vulkan resource creation, descriptor binding and dispatch;
DeltaShader owns only the final artifact and resolved ABI.
