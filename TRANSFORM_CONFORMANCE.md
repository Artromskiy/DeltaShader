# CPU/GPU transform conformance

This document covers authoring/lowering conventions and the concrete binary
layout they produce. It does not add fields or source-language types to the
final artifact contract.

## Authoring and GLSL lowering

The first indexed-mesh path uses a left-handed world convention with column
vectors. CPU authoring code and generated GLSL evaluate:

```text
clip = Projection * View * Model * float4(position, 1)
```

At compile time, `DeltaMaths.float4x4` maps by Roslyn symbol identity to GLSL
`mat4`. It is represented as four sequential `float4` columns. Each column has
16-byte alignment and the std430 matrix stride is 16 bytes.

For the current transform push-constant block, lowering resolves this layout:

| member | authoring/GLSL type | offset | size | matrix stride |
| --- | --- | ---: | ---: | ---: |
| `Model` | `float4x4` / `mat4` | 0 | 64 | 16 |
| `View` | `float4x4` / `mat4` | 64 | 64 | 16 |
| `Projection` | `float4x4` / `mat4` | 128 | 64 | 16 |

The complete block is 192 bytes and aligned to 16 bytes.

## Final runtime handoff

Before the shader reaches DeltaRender, the compiler must erase the Roslyn
symbol, `DeltaMaths.float4x4` and GLSL `mat4` identities. The canonical
`DeltaShader.Contract.ShaderAbi` carries only the resolved push-constant range
and concrete member offsets, sizes, alignments and matrix strides.

Application-side code packs the four columns in order into bytes; it must not
transpose them. DeltaRender binds those packed bytes according to the artifact
ABI. It does not infer layout from the CLR type or GLSL text.

Vulkan projection depth is `0..1`, supplied by the left-handed projection
helper. Screen Y is renderer viewport policy (negative viewport height where
supported), not an extra matrix transpose or a field in the shader artifact.
Camera code supplies Model, View and Projection values; mesh code owns
vertex/index data; DeltaRender owns Vulkan binding and draw recording.
