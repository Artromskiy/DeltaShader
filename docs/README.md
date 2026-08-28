# DeltaShader

DeltaShader is the first-party C# shader compiler for Vulkan GLSL 460 and
SPIR-V. Static attributed C# methods are validated with Roslyn and lowered
through a typed IR.

Shader authoring uses one `in` context value per entry point. User-defined
context structs declare resources, push constants, and graphics stage data as
explicitly annotated fields; execution builtins are accessed through
`ShaderBuiltins`.

## Boundaries

- `DeltaShader` / `Delta.Shader` is the user-facing C# shader authoring API.
- `DeltaShader.Contract` / `Delta.Shader.Contract` is the frozen runtime
  contract consumed by other first-party projects.
- Compiler, GLSL backend, analyzers, tool, and text projects are implementation
  or tooling; they are not runtime contract assemblies.

Public documentation:

- [USER_API.md](USER_API.md) describes C# shader authoring.
- [CONTRACT.md](CONTRACT.md) indexes the frozen DeltaShader-to-consumer
  artifact contract.
- [repository-layout.md](repository-layout.md) defines the project
  folder and project-boundary layout.
- [diagnostics.md](diagnostics.md) lists compiler and analyzer
  diagnostics.

Developer documentation:

- [INTERNAL.md](INTERNAL.md) describes compiler and publication boundaries.
- [../WORKFLOW.md](../WORKFLOW.md) contains bounded build, test and validation
  commands.
- [../TODO.md](../TODO.md) contains selected migration work.

The playground guide is in [playground.md](playground.md), and the contract
project notes are in [contract-project.md](contract-project.md).
