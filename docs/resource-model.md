# Shader resource model

`Delta.Shader.Abstractions` is the only public resource and artifact contract.
Resources are explicit entry-point parameters with stage-aware attributes:

- read-only/read-write storage buffers use std430 and explicit set/binding;
- push constants and specialization constants carry manifest layout metadata;
- sampled 2D textures use an opaque sampled-resource layout;
- stage builtins and vertex/fragment interfaces are explicit attributes.

The generated manifest records stage, set, binding, access, shader type,
offset/alignment/size and applicable array/matrix strides. Consumers use this
metadata directly. DeltaRender owns Vulkan buffers/images/descriptors and must
not infer layout from CLR reflection or define a second manifest.

Unsupported resource categories are diagnostics until validation, typed IR,
GLSL lowering, manifest serialization and SPIR-V tests all exist.

