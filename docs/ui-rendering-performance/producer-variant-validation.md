# UI producer variant validation

`UiShaderVariantCatalog` is a finite semantic allowlist. It does not discover
Render projects, source files, generated C# or artifacts. Concrete UI producer
publication remains owned by `DeltaRender.UIShaders`.

Before publishing a variant, the producer passes the catalog allowlist and its
prepared entries to `UiShaderVariantProducerValidator.Validate`. Each
allowlisted key must have exactly one entry containing:

- the producer C# source path;
- the generated program path, `VertexAbi`/`FragmentAbi` accessors and their
  generated vertex/fragment packers;
- non-empty vertex and fragment `.spv` paths;
- explicit vertex and fragment entry points;
- a deterministic `LayerSetIdentity` describing the ordered producer layers.

The result is strict accounting: `ValidatedVariants`, `MissingVariants` and
`UnsupportedVariants`. Missing concrete producer entries are not silently
prepared. Unsupported keys are rejected through catalog `TryValidate` with
`DSH019`, even when files exist.

One concrete artifact identity cannot be reused by different variant keys. This
prevents an all-effects program from being silently aliased to variants whose
layer sets should have been lowered independently.

The current producer handoff contains 12 visual and 12 text entries. The
effect variants include independent prepared pairs such as:

- `Visual/Rounded/OuterShadow`: rounded visual layer set with analytic outer
  shadow;
- `Text/Sdf/Glow`: SDF text layer set with analytic glow.
- `Visual/Rounded/InsetShadow`: rounded visual layer set with analytic inset
  shadow;
- `Text/Msdf/Glow`: MSDF text layer set with analytic glow.
- `Visual/Solid/Stroke`: solid visual layer set with analytic stroke;
- `Visual/Solid/Glow`: solid visual layer set with analytic glow;
- `Text/Msdf/Outline`: MSDF text layer set with analytic outline;
- `Text/Sdf/OuterShadow`: SDF text layer set with analytic outer shadow.
- `Visual/Solid/OuterShadow`: solid visual layer set with analytic outer shadow;
- `Visual/Rounded/Stroke+OuterShadow`: rounded visual layer set with analytic
  stroke and outer shadow;
- `Text/Msdf/OuterShadow`: MSDF text layer set with analytic outer shadow;
- `Text/Sdf/Outline+OuterShadow+Glow`: SDF text layer set with analytic outline,
  outer shadow and glow.
- `Visual/Rounded/Stroke+Glow`: rounded visual layer set with analytic stroke and
  glow;
- `Text/Msdf/Outline+OuterShadow+Glow`: MSDF text layer set with analytic outline,
  outer shadow and glow.

Each entry in this finite matrix has its own generated program identity, resolved
vertex and fragment ABI, ABI-derived packers and artifact pair. DeltaShader
validates that handoff but does not manufacture Render-owned source or
artifacts. A missing entry is an explicit missing disposition; it must not be
replaced by an all-effects alias, transform fallback or another variant's
program pair.

This is a build/editor-time handoff check. It performs no runtime probing,
reflection, transform insertion or runtime shader composition. Render receives
the already prepared artifact and resolved ABI; it does not infer this manifest
or duplicate packing/layout logic.

Text `CachedMask` remains Render-owned and is not a DeltaShader variant.
