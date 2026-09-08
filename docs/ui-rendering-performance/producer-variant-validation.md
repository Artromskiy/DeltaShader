# UI producer variant validation

`UiShaderVariantCatalog` is a finite semantic allowlist. It does not discover
Render projects, source files, generated C# or artifacts. Concrete UI producer
publication remains owned by `DeltaRender.UIShaders`.

Before publishing a variant, the producer passes the catalog allowlist and its
prepared entries to `UiShaderVariantProducerValidator.Validate`. Each
allowlisted key must have exactly one entry containing:

- the producer C# source path;
- the generated program path and `VertexAbi`/`FragmentAbi` accessors;
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

This is a build/editor-time handoff check. It performs no runtime probing,
reflection, transform insertion or runtime shader composition. Render receives
the already prepared artifact and resolved ABI; it does not infer this manifest
or duplicate packing/layout logic.

Text `CachedMask` remains Render-owned and is not a DeltaShader variant.
