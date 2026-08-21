# DeltaShader language subset

The supported language is intentionally smaller than C# and validated through
Roslyn symbols/`IOperation` before lowering.

Supported constructs include:

- static compute, vertex and fragment entry methods;
- scalar/vector arithmetic, comparisons and supported conversions;
- locals, assignments, conditionals and structured bounded loops;
- static non-recursive helpers;
- explicit buffers, constants, textures, stage builtins and stage interfaces;
- supported `Delta.Maths` constructors, operators, swizzles and functions
  resolved by symbol identity.

Rejected constructs include reference types in shader-visible state, managed
captures/closures, allocation, exceptions, async, reflection, dynamic,
recursion and virtual/interface dispatch. `double` and `fix` are CPU-only until
a target profile and lowering are explicitly implemented.

The production authoring form is `[DeltaCompute]`, `[VertexShader]` or
`[FragmentShader]` on a compile-time static method. Runtime values enter through
explicit resources or constants, not captured variables. See
`../src/Delta.Shader.Analyzers/AnalyzerReleases.Unshipped.md` and
`diagnostics.md` for diagnostic IDs.
