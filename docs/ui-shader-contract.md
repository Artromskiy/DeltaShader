# DeltaShader.UI rectangle contract

`DeltaShader.UI` owns the canonical authoring sources for the solid and rounded
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
| `rounded-rectangle-slice` vertex + fragment | `RoundedRectangleSliceGraphicsShaderProgram` |

Each program is a six-vertex rectangle draw. The vertex stage reads one record
per instance using `ShaderBuiltins.InstanceIndex`. The fragment stage receives
the selected record values through the interstage payload. There is no per-
rectangle push-constant update and no one-draw-per-rectangle requirement.

## Resource ABI

Both programs use one resource in the vertex stage:

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

## Rounded rectangle slice path

`RoundedRectangleSliceGraphicsShaderProgram` is the producer-side fast path for
large ordered UI batches. The host expands one logical rounded rectangle into
nine six-vertex instances: center, four straight edges and four corner
regions. All nine records share one set-0/binding-0 read-only storage buffer
and use `ShaderBuiltins.InstanceIndex`; Render can submit them as one ordered
instanced batch rather than nine draw calls per rectangle. Zero-area regions
may be omitted by the host.

`RoundedRectangleSliceParameters` has base alignment `16`, size `96` and array
stride `96` bytes:

| Field | Type | Offset | Size |
| --- | --- | ---: | ---: |
| `FillColor` | `float4` | 0 | 16 |
| `BorderColor` | `float4` | 16 | 16 |
| `CornerRadii` | `float4` | 32 | 16 |
| `SegmentRect` | `float4` | 48 | 16 |
| `CornerData` | `float4` | 64 | 16 |
| `BorderWidth` | `float` | 80 | 4 |
| trailing std430 padding | - | 84 | 12 |

`SegmentRect` is the sub-quad in top-left pixel units. `CornerData` stores
corner-center X/Y, radius and `isCorner` in W (`0` for center/edge regions,
`1` for corner regions). Corner radii remain ordered TL, TR, BR, BL. The
corner path evaluates a circle distance only for corner regions; center and
edge regions use straight-boundary distances. Fill and border remain
premultiplied-alpha contributions. The classic program retains Render's
scissor as its clip authority. The clip-aware programs below carry the
effective clip in every instance and use a fragment discard, allowing
non-adjacent clip regions to share one ordered instanced draw without making
Render compute shader layout.

The generated packer is
`PackRoundedRectangleSliceVertexInstancesElements`; no Render-local byte
layout or packer is permitted. The decomposition requires a Render batching
adapter to expand records and preserve painter order; it does not require a
new frozen `ShaderAbi` contract.

The producer-side `RoundedRectangleSliceBuilder.Build` helper accepts one
`RoundedRectangleParameters` value and writes up to nine normalized records to
a caller-owned `Span<RoundedRectangleSliceParameters>`. It clamps negative
width/height and border width to zero, scales oversized corner radii uniformly
so adjacent radii fit the rectangle, and omits zero-area regions. Its output
must then be passed to the generated `InstancesElements` packer; the builder
does not write bytes or replace the ABI packer.

### Per-instance clip-aware programs

`ClipAwareSolidRectangleGraphicsShaderProgram`,
`ClipAwareRoundedRectangleGraphicsShaderProgram` and
`ClipAwareRoundedRectangleSliceGraphicsShaderProgram` are the producer-owned
variants for ordered UI streams containing different effective clips. Each
vertex context has one set-0/binding-0 read-only storage buffer named
`Instances`; each fragment stage receives `ClipRect` through the generated
interstage payload. `ClipRect` is `x, y, width, height` in top-left UI pixel
coordinates. Render intersects nested clips before creating each instance and
the shader discards fragments outside that effective rectangle.

The generated instance layouts are:

| Program | `ClipRect` offset | Record size/array stride |
| --- | ---: | ---: |
| `ClipAwareSolidRectangle` | 32 | 48 |
| `ClipAwareRoundedRectangle` | 80 | 96 |
| `ClipAwareRoundedRectangleSlice` | 96 | 112 |

All values are bytes and are resolved from `ShaderAbi`; no manual padding or
host-side layout code is permitted. The rounded slice variant is created with
`RoundedRectangleSliceBuilder.BuildClipAware(in rectangle, clipRect,
destination)`, then packed with the generated
`PackClipAwareRoundedRectangleSliceVertexInstancesElement(s)` helper. The
frame-wide `UiFrameConstants.Resolution` remains the only push-constant data;
clip rectangles are never push constants.

## Ownership boundary

`DeltaShader.UI` owns these shader sources, resolved ABI metadata, generated
SPIR-V and generated packers. `DeltaRender` owns buffers, descriptors, pipeline
creation, device limits and draw submission. `DeltaXAML` or Engine provides
ordinary CLR value records and does not know std430 offsets or Vulkan types.
