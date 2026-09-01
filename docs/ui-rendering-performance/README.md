# Production Vulkan UI rendering pipeline

Status: DeltaShader owner design guidance. This document is an implementation
policy, not a new runtime contract and not a replacement for the frozen
`ShaderArtifact`/`ShaderAbi` contract.

DeltaShader targets one renderer path: Vulkan GLSL 460 and SPIR-V. References to
Skia, Valve, and Unity below are production evidence for algorithms and cache
policies, not additional backend commitments.

The target is predictable frame time and high throughput for retained text,
solid and rounded rectangles, images, clipping, outlines, shadows, glows, and
animated parameters. Draw count alone is not the objective. The complete budget
includes command recording, state changes, upload bytes, atlas misses, shaded
pixels, overdraw, and fragment cost.

## Ownership

- `DeltaShader` owns C# shader authoring, lowering, final SPIR-V artifacts,
  resolved `ShaderAbi`, and generated pack/unpack helpers.
- `DeltaText` owns shaping, glyph selection, rasterization policy, and glyph
  atlas production.
- `DeltaXAML` owns semantic paint, layout, display-list order, and clip
  identity.
- `DeltaRender` owns Vulkan buffers, descriptors, pipelines, atlas residency,
  scissor/stencil state, uploads, and draw submission.
- Engine and Editor provide ordinary CLR values and select or cache prepared
  shader variants. They do not calculate `std430` offsets or reinterpret the
  shader ABI.

C# compilation and shader composition happen during build or Editor preparation.
The frame loop consumes a prepared artifact and never compiles C# or performs
reflection over shader contexts.

## Production decisions

These are the defaults for the Vulkan path:

1. Use one small artifact per render class instead of one branch-heavy
   uber-shader.
2. Store per-item values in persistent instanced storage buffers. Use push
   constants only for small frame-wide values.
3. Pack all GPU-visible data through generated helpers derived from
   `ShaderAbi`.
4. Batch contiguous painter-order records by GPU state.
5. Use scissor for rectangular clips and stencil or a mask only for complex
   clips.
6. Use the cheapest artifact that expresses the visual result.
7. Move large or reusable effects to cached masks instead of multiplying
   fragment work for every item.
8. Accept an optimization only after comparing GPU fragment time, overdraw, and
   upload cost with the reference path.

## Frame pipeline

### 1. Normalize the display list

Convert each visual into a small render class before command recording:

| Class | No-effect path | Effect path |
| --- | --- | --- |
| Solid rectangle | `Solid` | `Rounded`, slice, or mask |
| Rounded rectangle | analytic `Rounded` | border or cached effect path |
| Text | bitmap or SDF atlas | SDF/MSDF effect variant or mask |
| Image | atlas/page quad | mask or explicit effect pass |

Per-item style values must remain data. They must not create a new pipeline or
push-constant update when the shader variant already supports them.

### 2. Form a batch key

The batch key contains only state that changes Vulkan binding or raster state:

```text
artifact and pipeline identity
blend mode and target color space
texture or atlas page
sampler policy
clip/scissor or stencil identity
effect variant and quality tier
```

Fill color, border color, opacity, radii, transform, UV rectangle, glyph
metrics, distance range, and effect parameters remain in instance records.

Preserve painter order. Merge only contiguous records with a compatible key.
Reordering is allowed only inside an explicitly reorderable layer where it
cannot change blending, clipping, hit-test-visible output, or effect
依dependencies.

### 3. Upload typed records

Use a persistent frame or ring allocation for instance and vertex data. Track
dirty display-list ranges and update only dirty contiguous ranges. Keep static
geometry in a reusable buffer; keep changing values in a per-frame instance
range.

The generated packer owns padding, alignment, array stride, matrix stride, and
future layout changes. Render must not reimplement the layout with `Marshal`,
`MemoryMarshal`, reflection, or a local byte writer.

### 4. Record compatible draws

The normal frame sequence is:

```text
update dirty instance ranges
bind pipeline, descriptor state, and clip state once per compatible run
pack frame-wide values once
issue one indexed or instanced draw per compatible contiguous run
```

The target is one draw per state-compatible run, not one draw per UI item. A
small number of larger batches is useful only when the fragment path remains
cheap and painter order is preserved.

## Data and ABI placement

### Instance data

Per-item data belongs in a storage buffer or instance vertex buffer. The
producer publishes one resolved layout and generated packer for the selected
artifact. A typical UI record contains only the fields needed by that artifact:

```text
rectangle or glyph geometry
premultiplied colors
radii and border width
UV rectangle and atlas page identity
distance range or effect parameters
```

Do not put a managed reference, CLR object identity, or service handle in a
shader-visible record.

### Frame data

Resolution, time, viewport scale, and other frame-wide values may use one
push-constant range when the resolved size fits the device limit. Otherwise use
a frame buffer. Push constants are not a replacement for per-item storage.

The [Vulkan push-constant guide](https://docs.vulkan.org/guide/latest/push_constants.html)
and [constant-data performance sample](https://docs.vulkan.org/samples/latest/samples/performance/constant_data/README.html)
show the small-range nature of push constants and the fact that the best choice
is device-dependent. The backend must query `maxPushConstantsSize` and select a
valid layout before submission.

### Final artifact boundary

The generated program must expose the final `ShaderArtifact`, resolved
`ShaderAbi`, stable program identity, and typed packers. The runtime boundary
must not contain Roslyn symbols, typed IR, GLSL source, compiler manifests, or
live CLR generic values.

For a composite shader, the Editor selects source layers first. The compiler
then emits one final graphics artifact, one resolved ABI, and one packer surface
for the composed program. Render consumes that result without knowing the
source-layer contexts.

## Solid and rounded rectangles

### Solid path

Use a reusable six-vertex quad or equivalent instanced geometry. The fragment
shader outputs premultiplied color and performs no SDF evaluation. This path is
the baseline for opaque and simple translucent rectangles.

### Analytic rounded path

Use one analytic signed-distance evaluation for ordinary rounded rectangles.
Keep independent radii in canonical order `TopLeft, TopRight, BottomRight,
BottomLeft`. Normalize the radii on the producer side so adjacent radii fit the
rectangle. Keep radius and border-width units explicit and consistent.

Use derivative-based antialiasing with `fwidth` at the boundary and clamp the
coverage transition to a finite range. Add fast paths for zero radii, zero
border, and regions known to be fully inside the shape.

### Slice path

`RoundedRectangleSlice` is the decomposition option for large visuals. One
logical rectangle becomes up to nine sub-quads in one instanced draw. Straight
regions use straight-boundary distances and corner regions use circle
 distances. This reduces expensive corner work over a large interior, but
increases instance records and covered geometry.

Keep both classic and slice artifacts. Choose between them from measured
fragment cost, shape size, and batch compatibility. Do not assume that nine
records are always faster.

### Border

For a thin border, derive inner and outer coverage from the same shape distance
and blend fill and outline once. Avoid a second full-quad pass for a simple
border. Use a dedicated variant or cached mask for wide strokes, complex joins,
or effects that would otherwise add branches to every rectangle.

### Clip

Use scissor whenever the clip is rectangular. Use stencil or a shader mask for
rounded, transformed, or otherwise non-rectangular clips. Batch by clip identity
and do not make every pixel pay for a complex clip that is not present.

## Text rendering

### Shape and cache on the CPU

Shape text once per content, font, size, script, and shaping-style change.
Cache shaped runs and retain glyph instance records. Upload positions, UVs,
atlas page, color, outline width, distance range, and effect parameters through
the generated artifact packer.

Skia is a useful production reference for this policy. Its GPU text path uses
glyph caches and distance-field text with explicit size thresholds, and falls
back to paths where distance fields are unsuitable. See [Skia GrTextContext](https://chromium.googlesource.com/skia/%2B/7c12e28cf414444a4d63b67ef0556f249287d702/src/gpu/text/GrTextContext.h),
[SkGlyphRunPainter](https://chromium.googlesource.com/skia/%2B/48b958b7094d4a156c9bb8822a816b91f53de628/src/core/SkGlyphRunPainter.cpp),
and [Skia glyph-cache options](https://chromium.googlesource.com/skia/%2B/a3e2996b08344a896884e6de050f7a2f2b80a409/include/gpu/GrContextOptions.h).

### Representation policy

Select representation by scale and quality rather than forcing one format:

| Representation | Preferred use | Limitation |
| --- | --- | --- |
| Bitmap atlas | fixed-size or very small glyphs | poor under large scale changes |
| Single-channel SDF | moderate scale changes | softer high-scale corners |
| MSDF | scalable text and sharp corners | more texture/setup work |
| MTSDF | effects needing a true signed distance | reserve for that requirement |
| Outline path | very large glyphs or unsupported transforms | more geometry and draw work |

The shader samples distance data as linear data and keeps distance-range units
explicit in the ABI. For MSDF, use the median of the distance channels and a
screen-space range. The [msdfgen documentation](https://github.com/Chlumsky/msdfgen#using-a-multi-channel-distance-field)
describes the median and `screenPxRange` requirements.

Valve's [distance-field text and special-effects paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf)
is the production reference for deriving antialiasing, outline, drop-shadow,
and related effects from a compact field.

### Text batches

Batch by artifact, atlas page, sampler, clip, and effect quality. A page change
is a normal batch boundary in the baseline Vulkan path. Descriptor indexing may
be added as a capability-gated optimization, but the ordinary explicit-page
path remains the compatibility baseline.

## Effects

Use a fixed effect family instead of an unbounded feature matrix:

| Effect | Small/local case | Large/expensive case |
| --- | --- | --- |
| Shadow | reuse shape/glyph distance with one offset | cached mask plus bounded blur |
| Glow | reuse distance with one soft band | cached mask or explicit effect pass |
| Outline | same-pass inner/outer coverage | dedicated stroke artifact or mask |
| Blur | one documented small kernel | separable cached mask pass |
| Distortion | one bounded transform/sample | cached or precomputed effect surface |

Small effects may remain in the same pass when their cost is bounded. Large
blur radii and multiple texture taps move to a cached mask or an explicit
pass. Never introduce an unbounded loop because a style value is large.

Animated effects update a compact frame or instance range. They must not force
atlas regeneration or pipeline creation unless the representation or resource
set changes.

## Vulkan-specific submission policy

The common path uses ordinary Vulkan descriptor sets and explicit atlas-page
batches. Descriptor indexing is optional because it adds capability and
non-uniform-indexing requirements. The [Vulkan descriptor-indexing sample](https://docs.vulkan.org/samples/latest/samples/extensions/descriptor_indexing/README.html)
is the reference for that optional path.

Indirect drawing is also optional. It is appropriate for a large GPU-generated
command list or visibility result, not as the default replacement for a small
CPU-built retained UI display list. Keep command recording simple until
instrumentation shows that CPU submission is the bottleneck.

All Vulkan layout decisions come from the final `ShaderAbi`. Device limits,
descriptor allocation, buffer lifetime, barriers, and draw submission remain
DeltaRender responsibilities.

## Editor composition

The Editor may compose reusable vertex, shape, text, and effect layers before
runtime:

1. select compatible layer entry points;
2. resolve semantic inputs and outputs;
3. flatten and remove unused interstage fields;
4. resolve one final resource and push-constant layout;
5. generate one artifact, one ABI, and typed packers;
6. cache by source identity, capability profile, quality tier, and effect
   variant;
7. hand the prepared program to Render.

Reject missing producers, conflicting semantic types, incompatible resources,
unsupported capabilities, and ambiguous effect ownership during preparation.
Runtime chooses among prepared variants but does not compile or reinterpret C#.

## Test and measurement matrix

Every artifact family needs compiler/golden tests for:

- no-effect solid rectangle;
- zero, uniform, and non-uniform rounded radii;
- thin and thick borders;
- clipped and unclipped shapes;
- text fill, outline, small shadow, and small glow;
- bitmap/SDF/MSDF selection and fallback;
- atlas-page and sampler batch breaks;
- dirty-range updates with unchanged records outside the range;
- ABI offsets, alignment, stride, stage visibility, and generated packers;
- composite input/output flattening and unused-field elimination.

Every optimization needs a reference comparison on target Vulkan devices. The
minimum visual set includes zero-effect, zero-radius, uniform radii, non-uniform
radii, border, clip, text outline, shadow, glow, and high-overdraw scenes.
Report image tolerance and timing separately. A compiler or artifact smoke is
not a GPU performance result.

Track:

```text
draws, instances per draw, pipeline binds, descriptor binds
atlas-page breaks, clip breaks, uploaded dirty bytes
vertex time, fragment time, shaded pixels, overdraw
atlas misses, glyph regeneration, mask-cache misses
effect pass count, display-list build time, command-recording time
```

## Implementation order

1. Instrument classic and slice rectangle paths without changing output.
2. Make persistent instance storage, dirty-range uploads, generated packers, and
   contiguous painter-order batching the default.
3. Keep solid, rounded, image, SDF text, and MSDF text as separate small
   artifacts with explicit quality and fallback choices.
4. Add same-pass thin border, small shadow, and small glow with bounded work.
5. Add cached masks for large blur and reusable complex effects.
6. Add optional descriptor indexing or indirect commands only after the baseline
   is measured on target Vulkan devices.
7. Tune resource placement and batch thresholds without changing the neutral
   shader authoring or final-artifact contract.

## Anti-patterns rejected

- one draw and one push-constant update per UI item;
- one universal uber-shader with every effect branch enabled;
- per-frame reflection over CLR contexts;
- manual ABI packing in Render or Engine;
- mandatory descriptor indexing in the baseline;
- large blur implemented as an unbounded fragment loop;
- an explicit effect pass for a small border or shadow that fits one distance
  evaluation;
- choosing an optimization by draw count without measuring fragment work,
  overdraw, upload bandwidth, and target-device frame time.

## References

- [Valve distance fields for text and effects](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf)
- [msdfgen multi-channel distance fields](https://github.com/Chlumsky/msdfgen#using-a-multi-channel-distance-field)
- [Skia GPU text context](https://chromium.googlesource.com/skia/%2B/7c12e28cf414444a4d63b67ef0556f249287d702/src/gpu/text/GrTextContext.h)
- [Unity UI Toolkit runtime performance](https://docs.unity3d.com/cn/2023.2/Manual/UIE-performance-consideration-runtime.html)
- [Vulkan push constants](https://docs.vulkan.org/guide/latest/push_constants.html)
- [Vulkan constant-data performance](https://docs.vulkan.org/samples/latest/samples/performance/constant_data/README.html)
- [Vulkan descriptor indexing](https://docs.vulkan.org/samples/latest/samples/extensions/descriptor_indexing/README.html)
