# DeltaShader runtime contract

This is the producer-owned index for the cross-project runtime artifact
boundary. It is not a C# shader authoring guide and does not expose compiler
implementation.

The frozen authoritative contract is
[final-artifact-contract.md](final-artifact-contract.md). Its CLR
declarations live only in `src/DeltaShader.Contract` under the
`Delta.Shader.Contract` namespace.

Consumers reference `DeltaShader.Contract` and consume final artifacts. They
do not depend on the compiler, Roslyn, typed IR, GLSL, JSON build manifests, or
tooling projects. C# shader authors use the separate `DeltaShader` project;
see [USER_API.md](USER_API.md).
