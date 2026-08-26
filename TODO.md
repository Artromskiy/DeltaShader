# DeltaShader TODO

## Selected migration

- Make compiler emission, generated compute/graphics factories, the CLI and
  reusable text factories produce `DeltaShader.Contract.ShaderArtifact` and
  its concrete `ShaderAbi` at the runtime boundary.
- Erase Roslyn/source identities, typed IR, GLSL text, profile strings, live
  generic values and compatibility JSON models before that handoff. GLSL and
  JSON may remain explicit build/inspection sidecars.
- Migrate DeltaRender and integration tests to consume only
  `DeltaShader.Contract.IShaderArtifact` and
  `DeltaShader.Contract.GraphicsShaderProgram`.
- After producer and consumer migration, remove the duplicate artifact,
  manifest and graphics-program types from `DeltaShader.Abstractions`.
- Keep static analyzer-driven authoring as the production API. Runtime
  Roslyn/lambda transpilation remains deferred tooling research and must not be
  presented as an implemented runtime path.

## Invariants

- `src/DeltaShader.Contract` and
  [docs/final-artifact-contract.md](docs/final-artifact-contract.md) are the
  source of truth for the immutable final SPIR-V and binary-ABI handoff.
- Do not add compiler, GLSL, CLR-object or Vulkan concerns to that contract.
- Do not introduce a second renderer manifest or graphics-program type.

Shared SDF/MSDF and graphics acceptance is tracked in
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
