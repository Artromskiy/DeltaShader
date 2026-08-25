# Delta.Shader.Contract

This project is the source of truth for the immutable final handoff from
DeltaShader to DeltaRender. The normative contract is documented in
[`../../docs/final-artifact-contract.md`](../../docs/final-artifact-contract.md).

Do not add Roslyn symbols, compiler IR, GLSL text, CLR `Type`, delegates,
reflection objects, live generic resource wrappers or Vulkan handles here.
The contract contains validated SPIR-V and its concrete serialized binary ABI.
Consumers compute their own cache keys when importing an artifact.
