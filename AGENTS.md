# DeltaShader agent router

Scope: Roslyn validation, typed shader IR, GLSL/SPIR-V generation and the
runtime-neutral final ShaderArtifact ABI. Compiler code must not depend on
Vulkan or DeltaRender. Map DeltaMaths by Roslyn symbol identity and generated
manifest, never by CLR-name guesses.

## Map — open only as needed

- ../CODE_STYLE.md — technical source-generation, ABI, ownership and hot-path rules.
- ../CONTRACTS.md — graphics-artifact ownership and direct consumers; open only for a boundary task.
- IDEAS.md — language/backend research/options only when requested.
- WORKFLOW.md — bounded build, tests, CLI and SPIR-V checks.
- docs/CONTRACT.md and docs/final-artifact-contract.md — frozen contracts; read-only unless the user requests revision.
- docs/USER_API.md — user-facing authoring API; open only for public API/documentation work.
- docs/INTERNAL.md — compiler implementation notes.
- src/DeltaShader.Contract and src/DeltaShader.* — production contract/compiler/backends.
- tests, samples, playground — verification, runnable examples and disposable experiments.

Use compiler-frontend, shader-dev, abi-and-calling-conventions, static-analysis
and code-generation-and-backends only for the matching bounded area. Do not
reintroduce compatibility artifact/manifest models when the frozen artifact
is the requested boundary.
