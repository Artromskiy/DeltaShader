# DeltaShader TODO

## Active compiler work

- [ ] Complete editor-selected composite chain lowering and materialization:
  resolve ordered layer reads/writes, forward omitted semantic fields, remove
  dead interstage values, and publish one final graphics artifact/ABI.
- [ ] Generate the final composite host projection and packer after chain
  lowering. It must remain derived from the selected `ShaderAbi`; the editor
  may cache an erased adapter, but runtime compilation and reflection-based
  packing remain out of scope.
- [ ] Keep extending the conformance publisher only from the current
  DeltaMaths handoff. Every supported identity needs either a validated
  artifact or an exact compiler/capability disposition; unsupported cases are
  not counted as passes.
- [ ] Add focused fixtures for newly admitted shader syntax or intrinsic
  mappings instead of broadening the language subset implicitly.

## Closed baseline

- [x] Compiler emission, generated factories, CLI and reusable text/UI
  producers publish `DeltaShader.Contract.ShaderArtifact` with resolved
  `ShaderAbi`. Roslyn, typed IR, GLSL and build manifests remain tooling
  representations or sidecars.
- [x] Legacy runtime artifact/manifest/program duplicates are removed from the
  public producer path. `Delta.Shader.Contract.GraphicsShaderProgram` is the
  canonical graphics program.
- [x] Generated std430 pack/unpack helpers cover push-constant roots, storage
  elements/ranges, and resolved vertex-buffer bindings without CLR raw-copy
  layout inference.
- [x] Graphics authoring uses semantic value types and one interstage payload;
  direct scalar/vector interstage fields are rejected.
- [x] Static helper call-graph lowering, expression-bodied methods, bounded
  loops, and `out` helper parameters have targeted compiler coverage.

## Ownership invariants

- `src/DeltaShader.Contract` and
  [docs/final-artifact-contract.md](docs/final-artifact-contract.md) are the
  source of truth for the immutable final SPIR-V and binary-ABI handoff.
- Do not add compiler, GLSL, CLR-object or Vulkan concerns to that contract.
- Do not introduce a second renderer manifest or graphics-program type.
- DeltaShader owns shader authoring, compiler validation, lowering, layout and
  generated packing. DeltaRender owns Vulkan allocation, descriptors,
  submission, device limits and GPU readback.
- Runtime Roslyn/lambda transpilation is deferred tooling research and must
  not be presented as an implemented runtime path.

Shared SDF/MSDF and graphics acceptance is tracked in
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
