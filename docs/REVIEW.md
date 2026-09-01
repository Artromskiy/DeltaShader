# DeltaShader review

Review date: 2026-09-01.

This is a contract-boundary status note. The normative final handoff is
[final-artifact-contract.md](final-artifact-contract.md) together
with `src/DeltaShader.Contract`.

## Current status

### Canonical final contract

`DeltaShader.Contract` defines the immutable final `ShaderArtifact`, concrete
binary `ShaderAbi` and validated vertex/fragment `GraphicsShaderProgram`. This
assembly has no Roslyn, compiler, GLSL or Vulkan dependency. Only this artifact
is intended to cross from DeltaShader to DeltaRender.

### Producer status

Within DeltaShader, the compiler, source generators, CLI, text factories and
tests publish the final contract types after source-language resolution. The
former compatibility artifact, manifest and graphics-program models are not
part of the producer or runtime path. Generated JSON and GLSL remain explicit
build/inspection sidecars; they are not an alternate renderer API.

### Tool outputs and runtime inputs are distinct

The CLI implements project loading, validation, GLSL emission and optional
external SPIR-V compilation/validation. Its `.glsl`, `.shader.json` and `.spv`
files are build outputs. GLSL and compatibility JSON remain useful for
inspection and packaging checks, but DeltaRender must receive the canonical
`ShaderArtifact`, not parse those sidecars as an alternative public API.

Generated typed packers may prepare packed bytes on the application side. No
final artifact may retain `System.Type`, generic resource objects, delegates,
reflection state, syntax trees, Roslyn symbols or compiler IR.

### Binary layout is consumer-facing; source types are not

The final ABI carries resolved resource/stage/access data and concrete offset,
alignment, size, array-stride and matrix-stride values. DeltaRender consumes
those values. Names such as `DeltaMaths.float4x4`, GLSL `mat4` and compiler
manifest strings belong to authoring/lowering and are erased before the final
runtime boundary.

## Historical baseline

The previous contents of this file described the 2026-08-18 restore, GLSL
syntax, entry-point, fixture and diagnostic review. Those findings were
subsequently remediated and are not a current task list. Selected remaining
work is maintained in [../TODO.md](../TODO.md); build and validation commands
live in [../WORKFLOW.md](../WORKFLOW.md).

## Historical legacy API

The former compatibility artifact, manifest, and graphics-program surface is
obsolete and removed. `Delta.Shader.Contract.GraphicsShaderProgram` is the
canonical final artifact program and is not obsolete; no compatibility facade
is retained.
