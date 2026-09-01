# Graphics vertical slice

The current compiler backend emits Vulkan GLSL `#version 460` as a build
intermediate and uses `std430` for structured storage and push-constant data.
The first graphics slice is intentionally small:

- `[VertexShader]` accepts one `in` context whose `[Interstage]` payload contains
  one `Delta.Shader.Position` field and optional location-based vertex input fields;
  `ShaderBuiltins.VertexIndex` remains available in the body.
- `[FragmentShader]` accepts one `in` context with the matching `[Interstage]`
  payload and returns one `float4` color. `ShaderBuiltins.FragmentCoord` and
  context `[PushConstant]` fields are available in the body.
- `ShaderIntrinsics.fwidth` and `DeltaMaths.maths.smoothstep` lower to
  fragment-stage GLSL operations. Using `fwidth` from a vertex shader produces
  a compiler diagnostic.
- C# source names are retained in compiler metadata as
  `SourceEntryPointName`; the emitted Vulkan entry point is `main`. Only the
  resolved entry point is copied to the final `ShaderArtifact.EntryPoint`;
  source names are not part of the renderer contract.

UI graphics authoring uses a top-left viewport convention: X increases to the
right, Y increases down, depth is `0..1`, and texture UV `(0, 0)` is the
texture's top-left corner. For a positive viewport, UI pixel coordinates use
`ndcX = 2 * x / width - 1` and `ndcY = 2 * y / height - 1`. The graphics
authoring path must not insert another Y inversion when the viewport already
uses this convention. UV orientation and framebuffer orientation are separate;
screen-space Y handling does not alter normal-map green-channel semantics.

The canonical source fixture is `tests/DeltaShader.TestShaders/Shaders/FullscreenUi.cs`.
The checked-in fixture project `tests/DeltaShader.FullscreenFixture` links that
source without duplicating it. It builds a fullscreen triangle from
`gl_VertexIndex` and renders an animated, anti-aliased rounded rectangle from
`Resolution`, `Time`, and `gl_FragCoord.xy`. No vertex buffer is required.

## Consumer contract

For inspection and packaging, the CLI can emit this build-side bundle per
stage:

```text
fullscreen-ui.vert.spv       fullscreen-ui.vert.glsl       fullscreen-ui.vert.shader.json
fullscreen-ui.frag.spv       fullscreen-ui.frag.glsl       fullscreen-ui.frag.shader.json
```

Generate this pair from a clean checkout with:

```bash
out_dir="$(mktemp -d)"
./eng/prepare-fullscreen-artifact.sh "$out_dir"
```

The script first builds the CLI and fixture, then validates the generated
modules. It fails when `glslangValidator`, `spirv-val`, or `jq` is unavailable;
it does not silently skip publication.

The `.glsl` and `.shader.json` files are compiler/build sidecars, not renderer
artifacts. The GLSL is compiled with
`glslangValidator -V --target-env vulkan1.2`, and the resulting SPIR-V is
validated with `spirv-val --target-env vulkan1.2`. Publication converts the
resolved compiler metadata into `DeltaShader.Contract.ShaderAbi` and creates
one immutable `ShaderArtifact` per stage. `GraphicsShaderProgram` then validates
and pairs the final vertex and fragment artifacts.

DeltaRender consumes only that final contract: `ShaderArtifact.Spirv` (or the
explicit `CopySpirv()` upload copy), `ShaderArtifact.EntryPoint`,
`ShaderArtifact.Stage`, and `ShaderArtifact.Abi`. Descriptor bindings come from
`Abi.Resources`; push-constant offsets and sizes come from `Abi.PushConstants`
and their `Layout`; stage interfaces come from `Abi.Inputs` and `Abi.Outputs`.
Vertex and fragment location interfaces must have matching concrete
`ShaderValueType` values. GLSL text, Roslyn state, CLR types, live generic
resource wrappers, reflection objects, packers, and content hashes do not cross
this boundary.

This slice does not implement swapchain/window/event-loop ownership or Vulkan
pipeline assembly; those remain renderer work. A compiler backend that writes
SPIR-V directly also remains future backend work. The use of a GLSL compiler
intermediate does not change the final SPIR-V-plus-binary-ABI renderer
contract.
