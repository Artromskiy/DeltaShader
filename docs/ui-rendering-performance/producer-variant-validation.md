# UI producer variant validation

`UiShaderVariantCatalog` is a finite semantic allowlist. It does not discover
Render projects, source files, generated C# or artifacts. Concrete UI producer
publication remains owned by `DeltaRender.UIShaders`.

The catalog also includes two resource-backed visual material keys without
effects:

- `Visual/Solid/LinearGradient`, analytic quality;
- `Visual/Solid/Image`, analytic quality.

These resource keys are deliberately narrow. Stroke, outer/inner glow, shadows and
`CachedMask` are not implicitly combined with them; such combinations produce a
stable `DSH019` disposition until a separate prepared variant is added.
Rounded resource variants are also outside the current prepared matrix:
`Rounded + LinearGradient` and `Rounded + Image` are rejected with the same
stable `DSH019` rather than selecting a Solid artifact or inserting a fallback.

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

The canonical producer handoff contains 15 visual and 16 text entries. The
effect variants include independent prepared pairs such as:

- `Visual/Rounded/OuterShadow`: rounded visual layer set with analytic outer
  shadow;
- `Text/Sdf/OuterGlow`: SDF text layer set with analytic outer glow.
- `Visual/Rounded/InnerShadow`: rounded visual layer set with analytic inner
  shadow;
- `Text/Msdf/InnerGlow`: MSDF text layer set with analytic inner glow.
- `Visual/Solid/Stroke`: solid visual layer set with analytic stroke;
- `Visual/Solid/OuterGlow`: solid visual layer set with analytic outer glow;
- `Text/Msdf/Stroke`: MSDF text layer set with analytic stroke;
- `Text/Sdf/OuterShadow`: SDF text layer set with analytic outer shadow.
- `Visual/Solid/OuterShadow`: solid visual layer set with analytic outer shadow;
- `Visual/Rounded/Stroke+OuterShadow`: rounded visual layer set with analytic
  stroke and outer shadow;
- `Text/Msdf/OuterShadow`: MSDF text layer set with analytic outer shadow;
- `Text/Sdf/Stroke+OuterShadow+OuterGlow`: SDF text layer set with analytic stroke,
  outer shadow and outer glow.
- `Visual/Rounded/Stroke+OuterGlow`: rounded visual layer set with analytic stroke
  and outer glow;
- `Text/Msdf/Stroke+OuterShadow+OuterGlow`: MSDF text layer set with analytic stroke,
  outer shadow and outer glow.
- `Visual/Rounded/InnerGlow`: rounded visual layer set with analytic inner glow;
- `Text/Sdf/InnerShadow`: SDF text layer set with analytic inner shadow.

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

Capability identities use the fixed order `Stroke`, `OuterShadow`,
`InnerShadow`, `OuterGlow`, `InnerGlow`. Stable names spell those capabilities
explicitly instead of encoding an opaque bit mask. The dense versioned values
are `0x01`, `0x02`, `0x04`, `0x08`, `0x10`. This is a breaking taxonomy
revision: old numeric masks are not parsed or migrated as current variant keys.
