# DeltaShader

DeltaShader is the first-party C# shader compiler for Vulkan GLSL 460 and
SPIR-V. Static attributed C# methods are validated with Roslyn and lowered
through a typed IR.

Public documentation:

- [USER_API.md](USER_API.md) describes shader authoring and the final consumer
  artifact contract.
- [docs/final-artifact-contract.md](docs/final-artifact-contract.md) is the
  frozen DeltaShader to DeltaRender handoff.
- [docs/diagnostics.md](docs/diagnostics.md) lists compiler and analyzer
  diagnostics.

Developer documentation:

- [INTERNAL.md](INTERNAL.md) describes compiler and publication boundaries.
- [WORKFLOW.md](WORKFLOW.md) contains bounded build, test and validation
  commands.
- [TODO.md](TODO.md) contains selected migration work.
