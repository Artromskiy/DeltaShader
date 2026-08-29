# UI shader contract

`DeltaShader.Ui` owns the C# authoring source for the first reusable UI
rectangle programs. It has no Vulkan, DeltaRender or DeltaXAML dependency.
The compiler emits GLSL 460 and the final producer boundary remains one
`ShaderArtifact` plus its resolved `ShaderAbi` per stage.

## Programs

The project contains two graphics pairs:

```text
solid-rectangle    SolidRectangleVertex / SolidRectangleFragment
rounded-rectangle  RoundedRectangleVertex / RoundedRectangleFragment
```

Each pair is drawn as a six-vertex triangle list with no vertex buffer. The
vertex shader uses `gl_VertexIndex` and converts top-left pixel coordinates to
clip space with `x * 2 - 1` and `1 - y * 2`. The emitted Vulkan entry point is
`main`; the C# names remain compiler metadata only.

## Solid rectangle ABI

There are no descriptors. Both stages use one push-constant block:

| Member | Type | Offset | Size |
|---|---|---:|---:|
| `Resolution` | `float2` | 0 | 8 |
| `Rect` | `float4` | 16 | 16 |
| `Color` | `float4` | 32 | 16 |

The block alignment is `16` and its size is `48` bytes. The eight-byte gap
after `Resolution` is part of the resolved ABI.

## Rounded rectangle ABI

There are no descriptors. Both stages use one push-constant block:

| Member | Type | Offset | Size |
|---|---|---:|---:|
| `Resolution` | `float2` | 0 | 8 |
| `Rect` | `float4` | 16 | 16 |
| `FillColor` | `float4` | 32 | 16 |
| `BorderColor` | `float4` | 48 | 16 |
| `CornerRadius` | `float` | 64 | 4 |
| `BorderWidth` | `float` | 68 | 4 |

The block alignment is `16` and its size is `80` bytes. `CornerRadius` and
`BorderWidth` are pixel-space values. The valid caller range is a non-negative
border width and a radius no larger than half the rectangle's smaller
dimension.

The fragment shader computes a pixel-space rounded-rectangle signed distance,
uses `fwidth` for analytic anti-aliasing, and derives the border as a finite
inner band:

```text
fillCoverage   = 1 - smoothstep(-edge, edge, distance)
innerCoverage  = 1 - smoothstep(-edge, edge, distance + BorderWidth)
borderCoverage = max(fillCoverage - innerCoverage, 0)
```

With `BorderWidth = 0`, the border contribution is zero. Clip regions are not
shader data: DeltaRender resolves them to renderer clip/scissor state.

## Ownership

DeltaShader owns the shader source, validation, lowering and final artifacts.
DeltaRender owns descriptor/pipeline creation, push-constant upload, draw
submission and resource lifetime. DeltaXAML owns UI bounds, paint semantics
and clip references; it does not know this ABI or Vulkan types.

The older source at
`DeltaRender/tools/DeltaRender.UiShaders/UiPanel.cs` was inspected but is not
modified in this producer-only slice. DeltaRender must remove or disable that
legacy producer in its own migration before the repository can claim there is
only one active UI shader source.
