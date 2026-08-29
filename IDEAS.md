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
  - Compose vertex modifiers and fragment/material layers in the compiler as a
    typed call graph; do not execute several Vulkan entry points for one draw.
  - Keep intermediate stage values as compiler IR. Emit one final vertex
    `Interstage` payload and one final fragment input interface, so Vulkan sees
    one `layout(location) out`/`in` pair per varying rather than an interface
    for every composed layer.
  - Map matching interstage fields automatically by stable member identity and
    exact shader type. Allocate deterministic locations after composition; do
    not confuse these locations with physical vertex-buffer `[Layout(location)]`
    declarations. An explicit mapping is only needed for an intentional rename
    or a stack whose payloads are not structurally compatible.
  - Preserve the special rule for `[Position] float4`: the final vertex value
    lowers to `gl_Position` and is not an ordinary varying. A fragment layer
    that needs position data must receive a separate explicitly declared field.
  - Merge resources and push constants from all layers into one resolved ABI;
    identical declarations may be shared, while set/binding or layout conflicts
    are compile-time diagnostics. The composite must not introduce a second ABI.
  - Require every layer input to be produced by an earlier layer or declared in
    the composite context. Missing fields, type mismatches, duplicate outputs,
    cyclic stage dependencies and ambiguous renames must fail before GLSL
    emission.
  - Lower helper functions by full Roslyn symbol identity and emit their call
    graph in dependency order. This is a future compile-time feature, not
    runtime shader compilation or a collection of renderer-specific wrappers.
