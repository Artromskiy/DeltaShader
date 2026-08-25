# Shader resource model

`Delta.Shader.Contract` is the final binary artifact contract.
`Delta.Shader.Abstractions` owns authoring resources, expressed as explicit
entry-point parameters with stage-aware attributes:

- read-only/read-write storage buffers use std430 and explicit set/binding;
- push constants and specialization constants carry manifest layout metadata;
- sampled 2D textures use an opaque sampled-resource layout;
- stage builtins and vertex/fragment interfaces are explicit attributes.

The generated manifest records stage, set, binding, access, shader type,
offset/alignment/size and applicable array/matrix strides. Consumers use this
metadata directly. DeltaRender owns Vulkan buffers/images/descriptors and must
not infer layout from CLR reflection or define a second manifest.

These values describe a serialized binary ABI after all C# types have been
erased. The manifest never transports live generic objects or `System.Type`.
Typed packers are optional host-side producers of bytes, not part of the final
artifact. The authoritative renderer handoff is documented in
[final-artifact-contract.md](final-artifact-contract.md).

Unsupported resource categories are diagnostics until validation, typed IR,
GLSL lowering, manifest serialization and SPIR-V tests all exist.
