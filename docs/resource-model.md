# Shader resource model

`DeltaShader.Contract` is the final binary artifact contract.
`DeltaShader` owns authoring resources, expressed as explicitly annotated
fields on one user-defined context struct per entry point:

- read-only/read-write storage buffers use std430 and explicit set/binding;
- push constants and specialization constants compile to concrete ABI layout
  metadata;
- sampled 2D textures use an opaque sampled-resource layout;
- stage builtins and vertex/fragment interfaces are explicit attributes.

These authoring attributes and generic resource wrappers are erased during
compilation. The compiler may record stage, set, binding, access, shader type,
offset/alignment/size and applicable array/matrix strides in an internal or
generated manifest, but that manifest is build metadata. Publication resolves
the same concrete information into the `ShaderAbi` carried by the final
`ShaderArtifact`.

DeltaRender consumes `artifact.Abi` directly. It owns Vulkan
buffers/images/descriptors and must not infer layout from CLR reflection,
consume the compiler's JSON/GLSL sidecars, or define a second manifest.

The final ABI values describe concrete binary layout after all C# types have
been erased. `ShaderArtifact` never transports live generic objects,
`System.Type`, Roslyn symbols, reflection state, or a content hash. Generated
typed packers are application-side producers of bytes derived from the same
resolved `ShaderAbi`; they are not part of the final artifact or DeltaRender.
The authoritative renderer handoff is documented in
[final-artifact-contract.md](final-artifact-contract.md).

## Generated host packing

The selected packing design keeps CLR authoring types convenient without
making their ordinary memory layout a GPU contract. DeltaShader tooling may
generate plain typed `Pack` and `Unpack` helpers for push constants, storage
buffer elements and vertex data. Those helpers write explicit ABI offsets,
padding, array strides and matrix strides from the resolved `ShaderAbi`.

The packers must not use CLR `sizeof`, `Marshal.SizeOf`, raw struct copies,
reflection or `MemoryMarshal` as a substitute for the resolved layout. They
must reject unsupported fields such as managed references and ordinary CLR
`bool` values unless the shader representation is explicitly defined. Matrix
packing preserves the contract's column-major convention without an implicit
transpose.

The generated helpers return or write packed bytes. A DeltaRender adapter
owns the Vulkan allocation, descriptor update, upload and readback; it
consumes those bytes and `ShaderArtifact.Abi` and does not recalculate the
layout. DeltaXAML and Engine provide semantic values such as paint and
placement data and do not know about packing, Vulkan or `std430`.

This is a tooling/application-side implementation boundary, not a second
runtime ABI. The existing `ShaderAbi` already carries the required resolved
layout metadata, so adding a packing-plan type to the frozen artifact contract
requires a separate contract revision and is not part of this design.

Unsupported resource categories are diagnostics until validation, typed IR,
GLSL lowering, compiler-metadata-to-`ShaderAbi` conversion and SPIR-V tests all
exist. Those compiler stages do not expand the final renderer artifact.
