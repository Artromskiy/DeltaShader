# ADR-0005: Symbol-identity based intrinsic registry for Delta.Maths and Delta.Shader intrinsics

## Context
Shader lowering must avoid brittle string matching for `Delta.Maths` types and methods.
Several user types/functions can share names (`dot`, `sin`, `normalize`) and only
Roslyn symbol identity is stable across overloads and aliases.

## Decision
`IntrinsicRegistry` becomes the single mapping point from Roslyn `ISymbol` to
backend intrinsic metadata for:

- `Delta.Maths.float2/3/4`, `Delta.Maths.int2/3/4`, `Delta.Maths.uint2/3/4`,
  `Delta.Maths.bool2/3/4` type symbols;
- vector constructors, user-defined operators and swizzle properties;
- selected `Delta.Maths.maths` methods (`sin`, `cos`, `tan`, `dot`, `normalize`).

Frontend and analyzers resolve these entities through symbol comparisons using
`SymbolEqualityComparer.Default` and never by plain string names for intrinsic
decisions.

## Consequences
- Overloads are handled safely because each symbol is stored separately.
- False positives from same-name collisions (e.g. custom `maths`-like types) are
  impossible without actual symbol identity match.
- `double` is not enabled by default in this MVP path; unsupported scalar/vector
  types are rejected during shader validation.

## Status
Accepted for 0.1 MVP.
