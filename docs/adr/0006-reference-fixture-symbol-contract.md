# ADR-0006: Project-reference fixture-based intrinsic contract tests

## Context
Symbol-based lowering decisions must be validated not only against inline syntax strings but against real compilations that come from `ProjectReference` graphs. This keeps symbol identity stable when types and methods are pulled transitively.

## Decision
Create a dedicated reference fixture project under `tests/Delta.Shader.Compiler.ReferenceFixtures` that depends on:

- `Delta.Maths`
- `Delta.Shader.Abstractions`

Compiler tests open this project through `MSBuildWorkspace` and validate that:

- `floatN` constructors,
- vector `op_*` operators,
- vector swizzle properties,
- `Delta.Maths.maths` calls

are all resolved through the shared `IntrinsicRegistry` by `ISymbol`, not by names.

## Consequences
- The contract for `IsSymbol` matching is regression-tested on realistic project inputs.
- Name-collision tests remain meaningful because fixture and user-defined symbols are kept distinct by assembly + namespace + symbol identity.
