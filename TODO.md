# DeltaShader TODO

## Active compiler work

- [ ] Migrate composite graphics lowering to separate stage contexts and
  semantic interstage ports. Contexts contain only stage-local resources,
  push constants, SSBO's; they must not be used as the
  interstage transport type nor vertex input.
- [ ] Resolve composite interstage ports by semantic leaf types rather than
  CLR payload names or enclosing structure identity. Build one physical
  interstage payload, forward fields omitted by a layer, remove unused fields,
  and emit diagnostics for conflicting types or missing producers.
- [ ] Treat `Position` as the required vertex semantic and `FragmentColor` as
  the required fragment result. At least one vertex/fragment layer must produce
  the corresponding semantic; the compiler generates `gl_Position` and the
  final fragment output without a separate user-authored terminal method.
- [x] Migrate every shader project, sample and compiler fixture to explicit
  stage ports: `Vertex(in VertexContext, in VertexPayload)` and
  `Fragment(in FragmentContext, in SurfacePayload)`. Keep stage-local data in
  the context and pass the semantic payload independently through the stage
  chain; intermediate fragment layers may return the same payload while final
  `FragmentColor` selection remains compiler-owned.
- [x] Remove the legacy combined-context entry-point API, including
  `Fragment(in FragmentContext)`, its analyzer/compiler compatibility paths,
  generated output, fixtures, tests and documentation. A migration is complete
  only when no shader project or consumer relies on the combined context.
- [ ] Generate the final composite host projection and packer after chain
  lowering. It must remain derived from the selected `ShaderAbi`; the editor
  may cache an erased adapter, but runtime compilation and reflection-based
  packing remain out of scope.
- [ ] Keep extending the conformance publisher only from the current
  DeltaMaths handoff. Every suppoLjrted identity needs either a validated
  artifact or an exact compiler/capability disposition; unsupported cases are
  not counted as passes.
- [ ] Add focused fixtures for newly admitted shader syntax or intrinsic
  mappings instead of broadening the language subset implicitly.

## UI composite variants

- [x] Keep a finite `UiShaderVariantCatalog` keyed by target, primitive,
  material, text representation, effect capabilities and quality. Unsupported
  combinations produce `DSH019`; transform is outside this catalog.
- [x] Expose `ShaderCompiler.PrepareUiVariant` for one preparation-time
  validation/composition call and return a deterministic identity containing
  both the capability key and ordered source/interface identity.
- [x] Expose `ShaderCompositeArtifactPublisher.CreateUiVariant` so tooling can
  retain that identity beside the frozen `GraphicsShaderProgram` without adding
  a second runtime ABI.
- [ ] Replace generated parameterless sidecar probing in shipping consumers
  with explicit artifact bytes; keep the explicit `CreateProgram(vertexSpirv,
  fragmentSpirv)` path as the canonical runtime entry.
- [ ] Finish producer migration to the current semantic payload shape and
  restore the compiler/Render shader test suites; current failures are tracked
  as producer fixture/API migration, not runtime fallback work.

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
- [x] Graphics authoring uses semantic value types and an explicit payload port;
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

Shared SDF/MSDF and graphics acceptance is tracked in the owning project
TODOs and [../CONTRACTS.md](../CONTRACTS.md).

## UI composite audit

- [x] Finite UI variant validation uses the public catalog `TryValidate` gate;
  unsupported keys produce stable `DSH019` and cannot be legalized by files.
- [x] Producer validation requires one deterministic source, generated program
  with `VertexAbi`/`FragmentAbi`, and non-empty vertex/fragment SPIR-V pair for
  every supplied allowlisted variant.
- [x] Composite liveness removes unused interstage fields from the physical
  interface; no transform or runtime composition path is introduced.
- [ ] Concrete `DeltaRender.UIShaders` source/artifact registration still has
  to be supplied to the validator by the producer handoff; DeltaShader does
  not discover Render files.
- [ ] Text `CachedMask` remains Render-owned and is not a DeltaShader variant.
