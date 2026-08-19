# ADR-0002: Backend-Neutral IR and Vulkan GLSL-first

## Context
The project needs a clear path to both backend-neutral compile reuse and a fallback SPIR-V route.

## Decision
IR is backend-neutral and captures typed values, control flow, resources, and requirements.
First backend is Vulkan GLSL 460 via external toolchain validation.

## Consequences
- Frontend and analyzer stay stable when SPIR-V backend is added later.
- We get deterministic GLSL text and immediate portability checks using `glslang`/`spirv-val`.
- Reflection contract is emitted from IR and validated against compiled SPIR-V artifacts.

## Status
Accepted for 0.1 MVP.
