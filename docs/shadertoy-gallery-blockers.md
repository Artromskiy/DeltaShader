# ShaderToy gallery blockers

This is an internal compiler/testing note, not a production feature promise.
The gallery may catalog a visual idea even when the current DeltaShader
authoring subset cannot express it. A blocked idea must remain marked as
blocked or unsupported in the catalog until the compiler and the external
Vulkan validation path support it.

## Current compiler boundary

The compiled fixtures use one explicit graphics context containing an
`[Interstage]` payload and a sequential `[PushConstant]` field, plus
`ShaderBuiltins.FragmentCoord` in the fragment body. Fragment color is the
`float4` entry-point return value. ShaderToy names such as `iResolution`,
`iTime`, `fragCoord`, `iChannel0`, and `mainImage` are not compiler inputs.
The current gallery therefore recreates visual ideas in terms of explicit
DeltaShader context values rather than copying ShaderToy wrappers.

## Missing functionality to track

| Capability | Why it blocks a direct or faithful port | Smallest useful implementation target |
| --- | --- | --- |
| Unsupported helper bodies in graphics | Static value-type helper call graphs, expression-bodied helpers and `out` helper parameters are covered by the current lowering; captures, recursion, unresolved calls and unsupported helper types remain blocked | Keep helper resolution by full Roslyn symbol identity and add a focused fixture only when a new supported construct is required |
| First-class scalar/vector `mod` contract | Many ShaderToy tiling examples use `mod`; the current safe subset can express common scalar wrapping as `x - floor(x / period) * period` | Add symbol-identity mappings for scalar and vector `mod` with Vulkan GLSL 460 validation |
| Texture declaration and sampling fixtures | `SampledTexture2D` exists, but a faithful ShaderToy image/noise port also needs a catalogued asset, format, sampler state, and deterministic test input | Add a fixture-owned texture manifest and a graphics test that binds an explicit sampler at a declared set/binding |
| Multipass feedback/state | Buffer A/B shaders depend on a previous-frame image; a single static fragment entry cannot model the ping-pong graph | Add an explicit multipass artifact graph and per-pass resource ABI before porting feedback examples |
| Derivatives beyond `fwidth` | Some examples need `dFdx`, `dFdy`, or texture LOD control; only the current owned `fwidth` intrinsic is part of the graphics slice | Add stage-gated derivative intrinsics and external validator coverage |
| Dynamic arrays/iteration and atomics | Particle, cellular-automata, and simulation examples often depend on data-dependent loops or atomics | Define a bounded-loop/resource/atomic profile and make the capability visible in the manifest |
| Full 3D ray-march helper library | Ray-marched ShaderToy scenes need reusable SDF helpers, camera transforms, normals, and sometimes shadow/AO helpers; copying a large source body would violate this fixture’s independent-authoring rule | Add small, symbol-mapped static helpers incrementally, each with a focused GLSL/SPIR-V test |

## Source availability note

The catalog was assembled from public ShaderToy view URLs and an accessible
metadata index. Direct source-page retrieval was unavailable in this
environment during cataloging (the site returned a challenge/payment response),
so every entry records that the license metadata is unverified. The 50 checked-in
implementations are independent high-level recreations, not direct source ports.
The catalog date is `2026-08-27`; the six-character ShaderToy view identifier is
retained for later human verification.

Audio/FFT inputs are intentionally outside the current DeltaShader contract;
they are not a pending implementation promise for the gallery.
