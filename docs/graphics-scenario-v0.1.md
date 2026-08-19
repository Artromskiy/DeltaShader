# Graphics vertical slice

Delta.Shader emits Vulkan GLSL `#version 460` and uses `std430` for structured
storage and push-constant data. The first graphics slice is intentionally small:

- `[VertexShader]` supports `[VertexIndex] uint`, one `[Position] out float4`,
  and location-based vector `[ShaderVarying] out` values.
- `[FragmentShader]` supports `[FragmentCoord] float2`, one
  `[FragmentColor] out float4`, matching varying inputs, and one sequential
  `[PushConstant]` struct.
- `ShaderIntrinsics.fwidth` and `smoothstep` are symbol-registered fragment-only
  intrinsics. Using them from a vertex shader produces a compiler diagnostic.
- C# source names are retained as `SourceEntryPointName`; emitted Vulkan entry
  point is `main` and is exposed as `EntryPointName`.

The canonical fixture is `tests/Delta.Shader.TestShaders/FullscreenUi.cs`. It
builds a fullscreen triangle from `gl_VertexIndex` and renders an animated,
anti-aliased rounded rectangle from `Resolution`, `Time`, and `gl_FragCoord.xy`.
No vertex buffer is required.

## Consumer contract

The CLI emits one independent artifact per stage:

```text
Vertex.spv       Vertex.glsl       Vertex.shader.json
Fragment.spv     Fragment.glsl     Fragment.shader.json
```

Each `.spv` is compiled with `glslangValidator -V --target-env vulkan1.2` and
validated with `spirv-val --target-env vulkan1.2`. A renderer passes
`ShaderArtifact.Spirv` and `Manifest.EntryPointName` (`main`) to Vulkan, selects
the pipeline stage from `Manifest.Stage`, binds resources from
`Manifest.Resources`, and writes push constants according to
`Manifest.PushConstants[*].Members`. Vertex and fragment location interfaces
must use matching locations and GLSL types.

This slice does not implement swapchain/window/event-loop ownership, Vulkan
pipeline assembly, or direct SPIR-V generation. Those remain consumer/backend
work while the compute path is preserved.
