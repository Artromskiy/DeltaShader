# ADR-0003: Explicit resource syntax and descriptor binding model

## Context
For GPU safety and predictable reflection, descriptors must be explicit and stable.

## Decision
Resources in GLSH are declared with explicit `{set, binding}` via attributes and wrapper types in `GLSH.Abstractions`.

## Consequences
- No auto-allocated bindings in MVP.
- Single-source mapping from C# declarations to manifest and descriptor sets.
- Analyzer can fail early on duplicate `(set, binding)` pairs.

## Status
Accepted for 0.1 MVP.

