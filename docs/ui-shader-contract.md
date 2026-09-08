# DeltaRender.UIShaders rectangle contract

`DeltaRender.UIShaders` owns the canonical authoring sources for the solid and rounded
rectangle graphics programs. The compiler emits the final
`Delta.Shader.Contract.ShaderArtifact` and resolved `ShaderAbi`; Render consumes
that artifact and the generated packers. Render must not reproduce this layout
with `Marshal`, `MemoryMarshal`, or a local byte writer.

## Coordinate conventions

The canonical UI coordinate system is:

- origin: top-left;
- positive X: right;
- positive Y: down;
- viewport origin: top-left;
- depth: `0..1`;
- texture UV `(0, 0)`: top-left;
- texture UV `(1, 1)`: bottom-right.

For a positive-viewport UI path, a pixel position is converted to normalized
device coordinates as follows:

```text
ndcX = 2 * x / width - 1
ndcY = 2 * y / height - 1
```

The shader must not add an arbitrary Y inversion when the viewport is already
configured for this top-left path. Texture UV orientation is independent from
framebuffer orientation. A screen-space Y choice does not imply a normal-map
green-channel flip; normal-map Y direction is explicit asset/material metadata,
and tangent handedness remains separate metadata. DPI scaling is intentionally
outside this contract and remains a separate backlog item.

## Programs

The stable entry-point names and generated program types are:

| Entry points | Generated program |
| --- | --- |
| `solid-rectangle` vertex + fragment | `SolidRectangleGraphicsShaderProgram` |
| `rounded-rectangle` vertex + fragment | `RoundedRectangleGraphicsShaderProgram` |
| `rounded-inner-shadow` vertex + fragment | `RoundedInnerShadowGraphicsShaderProgram` |
| `rounded-inner-glow` vertex + fragment | `RoundedInnerGlowGraphicsShaderProgram` |

Other finite effect combinations follow the same canonical
`<Primitive><EffectSet>GraphicsShaderProgram` and kebab-case entry-point naming.
There is no generic all-effects program or compatibility alias.

Each visual program is a six-vertex rectangle draw. The vertex stage reads one record
per instance using `ShaderBuiltins.InstanceIndex`. The fragment stage receives
the selected record values through the interstage payload. There is no per-
rectangle push-constant update and no one-draw-per-rectangle requirement.

## Resource ABI

Instance-backed visual programs use one resource in the vertex stage:

| Set | Binding | Kind | Access | Stages | Layout |
| ---: | ---: | --- | --- | --- | --- |
| 0 | 0 | storage-buffer | read-only | vertex | std430 |

The resource is named `Instances`. The generated resource type is
`ReadOnlyStorageBuffer<SolidRectangleParameters>` for the solid program and
`ReadOnlyStorageBuffer<RoundedRectangleParameters>` for the rounded program.

### SolidRectangleParameters

The record has base alignment `16`, size `32`, and array stride `32` bytes:

| Field | Type | Offset | Size |
| --- | --- | ---: | ---: |
| `Rect` | `float4` | 0 | 16 |
| `Color` | `float4` | 16 | 16 |

### RoundedRectangleParameters

The record has base alignment `16`, size `80`, and array stride `80` bytes:

| Field | Type | Offset | Size |
| --- | --- | ---: | ---: |
| `Rect` | `float4` | 0 | 16 |
| `FillColor` | `float4` | 16 | 16 |
| `BorderColor` | `float4` | 32 | 16 |
| `CornerRadii` | `float4` | 48 | 16 |
| `BorderWidth` | `float` | 64 | 4 |
| trailing std430 padding | - | 68 | 12 |

`CornerRadii` is ordered top-left, top-right, bottom-right, bottom-left.
`BorderWidth` and the radii use the same pixel-space units as the rectangle
record. The trailing 12 bytes are reserved by the struct-size rounding rule;
the generated packer clears them and does not expose them as CLR fields.

## Push constants

Only frame-wide data is pushed. Both vertex stages expose one push-constant
range rooted at `Frame` with `UiFrameConstants.Resolution` at offset `0`, size
`8` bytes, and alignment `8`. The fragment stages have no push-constant range.

Each finite analytic visual variant reads its instance record from set `0`,
binding `0`; the fragment stage has no resource and receives only its selected
geometry, fill and effect leaves through typed interstage fields. The canonical
effect taxonomy is `Stroke`, `OuterShadow`, `InnerShadow`, `OuterGlow` and
`InnerGlow`. Backdrop blur and `CachedMask` are outside analytic artifacts.

Outer shadow and outer glow expand the raster rectangle by their offset,
spread and blur radius while remapping UVs back to the original shape. Inner
shadow and inner glow remain clipped to the original shape coverage. All five
reuse the same signed-distance evaluation and derivative antialiasing.

### Prepared effect parameters

There is no shared all-effects instance record. Each prepared variant includes
only its declared effect layers and receives a separately resolved std430
layout, `ShaderAbi`, generated program identity and typed packer. Exact combined
variants may share one SDF evaluation, but they do not expose undeclared effect
fields or runtime feature switches.

The producer-side records mirror the selected fields of
`Delta.XAML.Contract.UiEffectParameters` without taking a DeltaXAML dependency.
The mapping is direct by canonical layer and field. The generated
`Pack<Variant>VertexInstancesElements` method is the only host-side byte writer
for each record. Effect leaves are interstage values, not a second host ABI.

The generated program exposes these direct root overloads:

```csharp
public static int PackSolidRectangleVertexFrame(
    in UiFrameConstants value,
    Span<byte> destination);

public static int PackRoundedRectangleVertexFrame(
    in UiFrameConstants value,
    Span<byte> destination);
```

Each also has a `byte[]` overload. The returned byte count is `8`.

## Generated instance packers

The resolved ABI is the only source for these methods. Each program exposes
the following overloads for its instance record:

```csharp
public static int PackSolidRectangleVertexInstancesElement(
    in SolidRectangleParameters value,
    Span<byte> destination);

public static int PackRoundedRectangleVertexInstancesElement(
    in RoundedRectangleParameters value,
    Span<byte> destination);
```

Each element method also has a `byte[]` overload. Array methods are named
`PackSolidRectangleVertexInstancesElements` and
`PackRoundedRectangleVertexInstancesElements`; they accept
`ReadOnlySpan<T>` and write a contiguous array using the resolved stride.
The array methods return `count * 32` bytes for solid records and
`count * 80` bytes for rounded records.

## Consumer flow

1. Load the packaged final artifact for the selected generated program.
2. Read `VertexAbi`/`FragmentAbi` from the generated program and use them for
   compatibility checks; do not create a second ABI model.
3. Allocate one set-0 binding-0 storage buffer with the generated array stride.
4. Pack all rectangle records with the generated `InstancesElements` helper.
5. Pack `UiFrameConstants` once per frame with the generated `VertexFrame`
   helper.
6. Bind the buffer and push range, then issue one instanced draw with
   `instanceCount` equal to the number of records.

The vertex shader uses the top-left pixel convention above and converts pixels
to clip space without a second Y inversion. Rounded coverage uses the four
independent radii and computes a finite border band; a zero
`BorderWidth` produces no border contribution.

## Clipping

Rounded and solid rectangles use the renderer-owned scissor boundary. DeltaShader
does not expose a separate clip-aware shader family or slice payload; the
consumer intersects nested UI clips and records the resulting scissor region.

## Ownership boundary

`DeltaRender.UIShaders` owns these shader sources, resolved ABI metadata, generated
SPIR-V and generated packers. `DeltaRender` owns buffers, descriptors, pipeline
creation, device limits and draw submission. `DeltaXAML` or Engine provides
ordinary CLR value records and does not know std430 offsets or Vulkan types.
