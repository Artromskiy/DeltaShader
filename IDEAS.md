# DeltaShader ideas

Not active work:

- Additional backends after the backend-neutral IR and Vulkan GLSL path are
  stable.
- Source-generated capture of explicitly declared compile-time constants.
- Runtime compilation service as a tooling layer; the production contract
  remains build-time static shader methods and explicit resources.
- Richer texture/image types after sampled 2D texture ownership is proven.
- Shader-local mutable value methods and property lowering:
  - Allow mutating instance methods on local mutable shader value types, lowering
    the receiver as GLSL `inout` state.
  - Keep ABI contexts, push constants, interstage payloads and resource handles
    immutable; resource writes remain explicit indexed writes.
  - Allow pure computed instance and static getters, including getters that are
    evaluated at shader runtime; do not add static mutable storage or static
    auto-properties.
  - Require explicit load, mutate and store when mutating a value read from an
    SSBO; initially reject implicit aliasing and `ref`/`out` receivers.
  - Reject mutation of `readonly` values, `in` contexts and static state with
    precise compiler diagnostics.
- Batched UI vertex data instead of one push-constant update per rectangle:
  - Add a separate batched solid/rounded UI shader ABI with a per-vertex or
    per-instance value payload for rectangle geometry, colors, corner radii and
    border width.
  - Generate typed vertex packers from the resolved `ShaderAbi`, while keeping
    XAML and Engine code unaware of byte offsets, stride and GPU memory.
  - Let DeltaRender retain a persistent GPU vertex/instance buffer, update only
    dirty element ranges by index, and issue one draw for each compatible batch.
  - Keep frame-wide values such as resolution in push constants updated once per
    frame; do not extend the current per-rectangle push-constant path implicitly.
  - Define the batching slice only after a real Render consumer exists; the
    current generated vertex packer supports one binding-0 stream and is not
    itself a batching implementation.
- Compile-time shader composite for typed stage composition:
  - Full design and proposed user surface: [docs/shader-composition.md](docs/shader-composition.md).
  - Compose ordered vertex and fragment/material layers into one logical typed
    dataflow and one final `ShaderArtifact`; do not execute several Vulkan
    entry points for one draw.
  - Match semantic fields by full type identity, treat payloads as typed patches,
    forward omitted fields, and remove dead interstage values before assigning
    per-composite locations.
  - Merge contexts, resources and push constants separately from interstage
    state. The resolved result remains the existing single `ShaderAbi`.
  - Keep missing producers, ambiguous writers, cycles and type conflicts as
    compiler diagnostics. This remains deferred compile-time work, not runtime
    shader composition.
