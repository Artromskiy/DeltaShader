# DeltaShader TODO

- Keep `Delta.Shader.Contract` as the immutable source of truth for final
  SPIR-V, binary ABI and `GraphicsShaderProgram`; never add compiler or Vulkan
  concerns to it.
- Migrate compiler emission and DeltaRender consumption from the compatibility
  artifact types in `Delta.Shader.Abstractions`, then remove those duplicates.
- Keep static analyzer-driven authoring as production API. Runtime Roslyn/lambda
  transpilation remains deferred tooling research and must be described
  consistently in README/playground/sample docs.

Shared SDF/MSDF and graphics acceptance is tracked in
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
