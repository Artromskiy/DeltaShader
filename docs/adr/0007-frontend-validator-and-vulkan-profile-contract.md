# ADR-0007: Frontend diagnostics and compiler host split for MVP

## Status
Accepted

## Context
`DeltaShader.Compiler` must be safe for analyzer reuse and should not depend on MSBuild project
loading at compile-time. The CLI needs real project loading to obtain `Compilation`,
while diagnostics for entry points and profile constraints must be explicit and fail fast.

## Decision
- `DeltaShader.Compiler` is now `netstandard2.0`-targeted and no longer references
  `Microsoft.CodeAnalysis.CSharp.Workspaces`.
- `DeltaShader.Tool` remains `net10.0` and is the explicit host for `MSBuildWorkspace`-based
  compilation.
- `check`/`emit` (`build` currently reuses `emit`) are implemented with a real
  compile → frontend → IR path:
  - parse/analyze project with Roslyn;
  - lower entry point metadata to `ShaderIrModule`;
  - emit Vulkan-style GLSL when valid.
- `Compute` entry lowering now rejects entry points that do not satisfy the
  one-context-field contract, instead of silently dropping undeclared data.
- Duplicate descriptor conflicts are reported with `DSH005`.
- Vulkan/SPIR-V profile validation now checks compatibility (`vulkan1.2` ↔ `SPIRV<=1.5`, etc.)
  and local workgroup bounds before emitting IR.
- GLSL emitter now always produces `void main()` for Vulkan compute entry execution and sanitizes
  resource identifiers against reserved words / collisions.

## Consequences
- Analyzer/CLI responsibilities are separated by target; analyzer-safe code can be consumed in
  analyzer and tests environments.
- `DeltaShader.Vulkan.Tests` includes a real external-tool compile/validate path:
  `glslangValidator` + `spirv-val`, with explicit skip when tools are unavailable.
