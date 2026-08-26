# ADR-0001: Roslyn IOperation-first Frontend

## Context
DeltaShader compiles a shader-like subset of C# and must preserve source-accurate diagnostics, including symbol-level overload resolution and attribute semantics.

## Decision
We implement the primary frontend as Roslyn-based and walk `Compilation` + `IOperation` as the authoritative IR source.

## Consequences
- Accurate overload resolution and conversion semantics from Roslyn.
- Precise spans and symbol identity for unsupported constructs.
- Analyzer and CLI share the same unsupported/allowed symbol and intrinsic decisions.
- No post-hoc C# compilation to IR from IL is performed.

## Status
Accepted for 0.1 MVP.

