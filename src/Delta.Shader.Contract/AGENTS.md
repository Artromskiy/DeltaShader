# Final artifact contract guard

Read [../../docs/final-artifact-contract.md](../../docs/final-artifact-contract.md)
before working in this directory.

This project is the immutable source of truth for the final
DeltaShader-to-DeltaRender binary handoff. Do not edit it during documentation
cleanup, compatibility work or producer/consumer migration. A change here
requires an explicit user request to revise the contract.

Never add Roslyn, compiler IR, GLSL text, CLR `Type`, live generic resource
objects, reflection, delegates, content hashes, Vulkan handles or DeltaRender
dependencies.
