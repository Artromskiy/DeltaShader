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
  manifest and graphics-program types from `DeltaShader`.
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

## Selected UI/text shader ownership

The neutral UI paint contract is `DeltaXAML`-owned; shader source and final
artifacts are `DeltaShader`-owned. This slice must not add Vulkan, SDL,
DeltaRender or XAML implementation dependencies to shader authoring.

- [ ] Create `src/DeltaShader.Ui/DeltaShader.Ui.csproj` and move the standard
  UI shader authoring source out of `DeltaRender/tools/DeltaRender.UiShaders`;
  leave no duplicate active producer after migration.
- [ ] Keep text shader source in `src/DeltaShader.Text/`; make SDF and MSDF
  fill/outline semantics explicit and document distance-range/outline-width
  units without changing existing artifacts silently.
- [ ] Add validated UI shader entry points for solid/rounded rectangles,
  borders and the first gradient/image paths using compact push constants or
  explicit resource bindings. Do not create an all-purpose parameter blob.
- [ ] Publish only `ShaderArtifact` plus resolved `ShaderAbi` to Render;
  generated `.spv`, `.glsl` and `.shader.json` remain artifact/package output,
  not hand-maintained source or a second runtime ABI.
- [ ] Add compiler/golden tests for descriptor layout, stage visibility,
  rounded coverage and outline behavior. Animation time/progress stays a
  host/render parameter, not retained XAML state.
