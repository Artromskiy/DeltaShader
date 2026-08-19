# ADR-0004: Default target profile

## Decision
MVP default targets:
- Vulkan: `1.2`
- GLSL: `460`
- SPIR-V: `1.5`

## Justification
Vulkan 1.2 + GLSL 460 are stable and sufficient for Vulkan-style compute without advanced extensions.

## Consequences
- Pipeline validation and reflection start from this profile by default.
- Future profiles (e.g., compute-only subset, mobile restrictions) can be introduced by compile options.
