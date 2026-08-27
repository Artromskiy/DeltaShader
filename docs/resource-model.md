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
`System.Type`, Roslyn symbols, reflection state, or a content hash. Typed
packers are optional application-side producers of bytes and are not part of
the final artifact or DeltaRender. The authoritative renderer handoff is
documented in
[final-artifact-contract.md](final-artifact-contract.md).

Unsupported resource categories are diagnostics until validation, typed IR,
GLSL lowering, compiler-metadata-to-`ShaderAbi` conversion and SPIR-V tests all
exist. Those compiler stages do not expand the final renderer artifact.
