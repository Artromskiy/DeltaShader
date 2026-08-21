# DeltaShader agent guide

Scope: Roslyn validation, typed shader IR, GLSL 460/SPIR-V generation and the
runtime-neutral `ShaderArtifact` ABI.

- [README.md](README.md) — stable public authoring/compiler contract.
- [TODO.md](TODO.md) — selected compiler work.
- [IDEAS.md](IDEAS.md) — deferred language/backend ideas.
- [WORKFLOW.md](WORKFLOW.md) — fast build, tests, CLI and SPIR-V checks.
- Read [docs/diagnostics.md](docs/diagnostics.md) for analyzer changes,
  [docs/graphics-scenario-v0.1.md](docs/graphics-scenario-v0.1.md) for graphics
  artifacts, and [TRANSFORM_CONFORMANCE.md](TRANSFORM_CONFORMANCE.md) for
  matrix/layout work. ADRs are decision records, not task lists.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) is authoritative for shared text
  and editor acceptance.

Compiler/abstractions must not depend on Vulkan or DeltaRender. Map Maths by
Roslyn symbol identity and generated manifest, never by CLR name guesses.

Skills: `compiler-frontend` for Roslyn/IOperation and diagnostics,
`shader-dev` for GLSL/stage semantics, `abi-and-calling-conventions` for std430
and manifests, `static-analysis` for analyzer rules, and
`code-generation-and-backends` for generated artifacts/backend lowering.
