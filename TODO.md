# DeltaShader TODO

- Keep `Delta.Shader.Abstractions` as the canonical owner of
  `GraphicsShaderProgram`; DeltaRender owns removal of its consumer duplicate.
- Make validated artifact/manifest state immutable and expose SPIR-V through a
  read-only representation plus an explicit upload accessor.
- Keep static analyzer-driven authoring as production API. Runtime Roslyn/lambda
  transpilation remains deferred tooling research and must be described
  consistently in README/playground/sample docs.

Shared SDF/MSDF and graphics acceptance is tracked in
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
