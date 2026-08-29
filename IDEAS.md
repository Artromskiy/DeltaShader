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
