# Final shader artifact contract

This document describes only the immutable handoff from DeltaShader to
DeltaRender. It is not the API of the analyzer, source generator, compiler,
typed IR, GLSL backend, CLI or authoring helpers.

```text
C# authoring source
  -> Roslyn validation and typed IR
  -> optional GLSL 460 inspection output
  -> SPIR-V compilation and validation
  -> ShaderArtifact { SPIR-V + resolved binary ABI }
  -> DeltaRender
```

Only `ShaderArtifact` crosses the runtime boundary. GLSL is an optional build
sidecar for inspection and validation; DeltaRender never consumes it.

## Minimal artifact

```csharp
public interface IShaderArtifact
{
    ShaderStage Stage { get; }
    string EntryPoint { get; }
    ReadOnlySpan<byte> Spirv { get; }
    ShaderAbi Abi { get; }
}
```

The canonical definitions live in `src/DeltaShader.Contract`. That project is
the only consumer-facing artifact surface.

The artifact does not carry a content hash. An immutable consumer such as
DeltaRender can compute and cache its own key from the SPIR-V and the ABI when
the artifact is imported. Packaging systems may maintain an external content
identifier, but that identifier is not part of the shader ABI.

## Binary ABI, not live C# types

The manifest describes the already-resolved binary contract: shader stage,
entry point, descriptor set/binding, resource kind and access, push-constant
ranges, vertex/stage interfaces, compute workgroup size, and concrete
offset/alignment/size/array-stride/matrix-stride values.

The original C# source and its types no longer exist at this boundary.
`ReadOnlyBuffer<T>`, `Texture2D<T>`, generic dispatch helpers and Roslyn symbols
are compile-time or host-side conveniences. They must be erased before the
artifact reaches DeltaRender. The final artifact contains no `System.Type`,
open or closed generic object, delegate, reflection object, syntax tree or
compiler IR.

Generated typed packers may exist on the application side, but their output is
packed bytes plus renderer resource handles. DeltaRender binds those resources
according to the serialized ABI; it does not reflect over the original CLR
object graph. The ABI layout metadata exists to validate and interpret binary
payloads, not to transport live C# values.

`GraphicsShaderProgram` is only a validated pair of final vertex and fragment
artifacts. It adds no source-language or intermediate representation data.
