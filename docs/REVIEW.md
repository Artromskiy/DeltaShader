# DeltaShader review

Review date: 2026-08-25.

This is a contract-boundary review, not a claim that the producer/consumer
migration is complete. The normative final handoff is
[final-artifact-contract.md](final-artifact-contract.md) together
with `src/DeltaShader.Contract`.

## Current findings

### Canonical final contract exists

`DeltaShader.Contract` defines the immutable final `ShaderArtifact`, concrete
binary `ShaderAbi` and validated vertex/fragment `GraphicsShaderProgram`. This
assembly has no Roslyn, compiler, GLSL or Vulkan dependency. Only this artifact
is intended to cross from DeltaShader to DeltaRender.

### Producers still use compatibility artifact types

The compiler model, source generators, CLI, text factories and current tests
still reference artifact and manifest types in `DeltaShader.Abstractions`.
Generated manifest JSON and GLSL constants therefore describe the current
authoring/compiler compatibility path. They are not the canonical renderer
handoff and must not be documented as one.

This is the active migration blocker: producers need to construct the contract
ABI after all source-language types are resolved, and consumers need to accept
that contract before the duplicate abstraction types are removed.

### Tool outputs and runtime inputs are distinct

The CLI implements project loading, validation, GLSL emission and optional
external SPIR-V compilation/validation. Its `.glsl`, `.shader.json` and `.spv`
files are build outputs. GLSL and compatibility JSON remain useful for
inspection and packaging checks, but DeltaRender must receive the canonical
`ShaderArtifact`, not parse those sidecars as an alternative public API.

Generated typed packers may prepare packed bytes and renderer handles on the
application side. No final artifact may retain `System.Type`, generic resource
objects, delegates, reflection state, syntax trees, Roslyn symbols or compiler
IR.

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
