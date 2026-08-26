# DeltaShader internal implementation notes

This document is internal. It is not a consumer contract and must not be used
to add a second runtime artifact model.

## Publication pipeline

```text
static C# method
  -> Roslyn validation and typed IR
  -> GLSL 460 sidecar
  -> glslangValidator and spirv-val
  -> final ShaderArtifact { SPIR-V + ShaderAbi }
```

The compiler targets `netstandard2.0` and therefore does not reference the
net10 runtime contract assembly. It produces an intermediate compiler
manifest. The net10 CLI resolves that manifest into `Delta.Shader.Contract`
objects before writing the SPIR-V output. Source generators use the same
resolved fields to emit constructors for the final contract directly; JSON is
never deserialized by generated runtime factories.

The final contract project is intentionally free of Roslyn, MSBuild, Vulkan,
renderer and ECS dependencies. `Delta.Shader.Abstractions` remains the
authoring/compatibility layer needed by the current compiler and existing
consumers until the recorded producer-consumer migration is complete.

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

## Ownership

DeltaShader owns compiler validation, lowering, final artifact construction
and ABI publication. DeltaRender consumes `IShaderArtifact` and
`IGraphicsShaderProgram`, creates Vulkan objects and owns resource lifetime.
MathsGen/Maths own the generated Maths declarations and shader contract used
by Roslyn symbol mapping. No renderer-specific types or copied ABI models are
added here.

GLSL and JSON remain explicit build/inspection sidecars. They are not an
alternate runtime boundary and are not required by a consumer that already
has final SPIR-V plus `ShaderAbi`.
