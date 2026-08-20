# CPU/GPU transform contract

The first indexed-mesh path uses a left-handed world convention with column
vectors. CPU and GLSL evaluate:

```text
clip = Projection * View * Model * float4(position, 1)
```

`Delta.Maths.float4x4` is stored as four sequential `float4` columns and maps
directly to GLSL `mat4`. Each matrix column has 16-byte alignment and a
16-byte std430 matrix stride.

For a transform push-constant block:

| member | GLSL type | offset | size | matrix stride |
| --- | --- | ---: | ---: | ---: |
| `Model` | `mat4` | 0 | 64 | 16 |
| `View` | `mat4` | 64 | 64 | 16 |
| `Projection` | `mat4` | 128 | 64 | 16 |

The complete block is 192 bytes, aligned to 16 bytes. Render must upload the
four columns in order and must not transpose the data. Vulkan projection depth
is `0..1`, supplied by the left-handed projection helper. Screen Y is handled
by the Vulkan viewport policy (negative viewport height where supported), not
by an extra matrix transpose or global Y-flip.

Camera code supplies Model, View, and Projection. Mesh code owns vertex/index
buffers; Render owns descriptor/push-constant binding and draw recording.
