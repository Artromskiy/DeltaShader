# DeltaShader internal implementation notes

This document is internal. It is not a consumer contract and must not be used
to add a second runtime artifact model.

The frozen consumer boundary is indexed by [CONTRACT.md](CONTRACT.md). This
document describes only how implementation projects publish that boundary.

## Publication pipeline

```text
static C# method
  -> Roslyn validation and typed IR
  -> GLSL 460 sidecar
  -> glslangValidator and spirv-val
  -> final ShaderArtifact { SPIR-V + ShaderAbi }
```

The compiler targets `netstandard2.0` and therefore does not reference the
net10 runtime contract assembly. It produces a build-time
`ShaderCompilationManifest`. The net10 CLI resolves that manifest into `DeltaShader.Contract`
objects before writing the SPIR-V output. Source generators use the same
resolved fields to emit constructors for the final contract directly; JSON is
never deserialized by generated runtime factories.

The final contract project is intentionally free of Roslyn, MSBuild, Vulkan,
renderer and ECS dependencies. `DeltaShader` contains authoring-only
attributes, builtins and resource declarations; it has no artifact, manifest
or dispatch API.

## ABI conversion rules

- resource categories map to final resource kinds by the compiler metadata
  category, not by CLR object inspection;
- stage and stage-mask values are mapped explicitly because the intermediate
  and final enum layouts are independent;
- storage layouts preserve resolved size, alignment, array stride, matrix
  stride and recursively resolved members;
- opaque sampled resources have an empty data layout;
- GLSL scalar, vector and matrix types become `ShaderValueType`; generated
  user structs become structure values with nested `ShaderAbiLayout`;
- the source entry-point name stays compiler metadata, while final Vulkan
  artifacts use emitted entry point `main`.

The conversion is intentionally strict. Unknown resource categories or ABI
types fail publication rather than producing a partially described artifact.

## Generated host packing

Typed host packing belongs to DeltaShader tooling and generated application
code, not to `DeltaShader.Contract` and not to DeltaRender's Vulkan layer.
The generator consumes the resolved layout already produced for `ShaderAbi`
and emits direct typed helpers for concrete authoring values:

- push-constant values are written at their resolved member offsets;
- storage-buffer elements use their resolved struct and array strides;
- vertex values use the resolved binding stride and attribute offsets;
- readback helpers decode the same layouts in reverse.

Generated code now exposes `Pack<Method>Context` and typed element/vertex
array helpers. It makes padding and ownership visible, preserves column-major
matrix semantics, and rejects managed/reference fields and ambiguous CLR
representations. It must not infer GPU layout from CLR sequential layout,
reflection, `Marshal.SizeOf`, raw struct copies or a second manifest.

The current producer emits packing and typed readback/unpack helpers for
writable payload values. The Render-side adapter receives packed bytes plus the final artifact. It owns
resource allocation, upload/readback, descriptors and device-limit checks;
DeltaShader does not reference Vulkan or DeltaRender. `UiTextDraw` and its
paint semantics remain producer data in DeltaXAML, while atlas pages, UVs,
glyph instances and distance-range interpretation remain Render-owned.

`ShaderAbi` already expresses the layout required by this design. No new
neutral runtime contract type is justified until an actual missing invariant
is demonstrated and separately approved.

## Ownership

DeltaShader owns compiler validation, lowering, final artifact construction
and ABI publication. DeltaRender consumes `IShaderArtifact` and
`IGraphicsShaderProgram`, creates Vulkan objects and owns resource lifetime.
DeltaMathsGen/DeltaMaths own the generated DeltaMaths declarations and shader contract used
by Roslyn symbol mapping. No renderer-specific types or copied ABI models are
added here.

GLSL and JSON remain explicit build/inspection sidecars. They are not an
alternate runtime boundary and are not required by a consumer that already
has final SPIR-V plus `ShaderAbi`.
