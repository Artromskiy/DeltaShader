# GLSH review

Review date: 2026-08-18.

Verdict: this document records the 2026-08-18 baseline review. The listed
restore, GLSL syntax, CLI, fixture, diagnostic, and external-validation findings
were subsequently remediated. GLSH is still not a complete 0.1 runtime slice:
shader-body lowering, manifest/reflection, and headless dispatch remain outside
the current implementation.

## Blocking findings

### 1. Analyzer target graph cannot restore

`GLSH.Analyzers` targets `netstandard2.0` but references `GLSH.Compiler`, which
targets only `net10.0`. `dotnet restore GLSH.sln` fails with `NU1201`.

The reusable analyzer/compiler rules need a `netstandard2.0`-compatible project
or compatible multi-targeting. MSBuildWorkspace/CLI-only code must remain in a
separate `net10.0` host project.

### 2. Emitted storage-buffer arrays use invalid GLSL declaration syntax

`GlslEmitter` emits `vec4[] data;`. GLSL array brackets belong after the
declarator: `vec4 data[];`. The golden test currently asserts the invalid form,
so it protects the defect instead of catching it.

### 3. The emitter does not emit a GLSL entry point

The output contains `void <CSharpEntryName>()` but no `void main()`. The original
name belongs in the manifest/source map; the default GLSL compilation path must
provide the source entry point expected by the selected glslang invocation.

### 4. No executable compiler or Vulkan vertical slice exists

The CLI `check` command always exits successfully, `emit` is a stub, the IR body
contains string placeholders, and the GLSL body is empty. `GLSH.Vulkan.Tests`
contains no test method. The current project therefore does not demonstrate
C# -> IR -> GLSL -> SPIR-V -> dispatch.

### 5. Entry-point parameters are accepted and then silently discarded

The frontend accepts ordinary scalar/vector parameters while the generated
module records only storage buffers. There is no built-in/resource lowering for
those values. A shader such as the current fixture can compile successfully in
the frontend while its `invocationIndex` and method body disappear from output.
Unsupported ordinary parameters must be diagnosed until a concrete built-in or
push-constant contract exists.

## Important findings

- Default options combine Vulkan 1.2 with SPIR-V 1.6. The target profile and
  SPIR-V version must be a validated compatible pair rather than independent
  strings.
- Tests open projects through absolute paths under
  `/Users/rum/GitProjects/TheFurnace`, preventing relocation and CI execution.
- `GLSH.Compiler.ReferenceFixtures` is not part of the solution/project graph,
  so a clean solution restore does not guarantee that the fixture is restored
  before `MSBuildWorkspace` opens it.
- Duplicate descriptor bindings are reported as `GLSH002`; the documented
  diagnostic contract assigns binding conflicts to `GLSH005`.
- Name sanitization only replaces spaces. It does not handle GLSL keywords,
  reserved prefixes, collisions, or the full identifier policy.
- Local workgroup sizes are read from Roslyn attributes but not validated
  against zero, target-profile dimensions, or total invocation limits.
- `GLSH.TestShaders/VectorAdd.cs` is not compiled by a project and contains
  nullable resource/null-check semantics that are outside the planned shader
  subset.

## Verification performed

`dotnet restore GLSH/GLSH.sln --nologo -m:1 /nodeReuse:false` was run after the
central package versions were corrected to Silk.NET 2.23.0 and Roslyn Analyzers
3.11.0. Package-version errors disappeared, exposing the analyzer/compiler TFM
incompatibility described above. Tests could not run until that restore blocker
is fixed.

## Recommended repair order

1. Split or multi-target the analyzer-safe compiler rules and obtain a clean
   restore/build/test.
2. Add a GLSL syntax/compile test and correct arrays plus `main` emission.
3. Reject silently discarded parameters and validate local size/profile pairs.
4. Make fixtures path-independent and part of the build graph.
5. Implement one real expression/buffer lowering path and a real CLI command.
6. Compile with pinned glslang, validate with `spirv-val`, and execute the
   headless Silk.NET Vulkan test before calling the milestone 0.1.

## Current status after remediation

The current GLSH solution has a clean restore and build graph. `GLSH.Compiler`
and `GLSH.Analyzers` target `netstandard2.0`; MSBuildWorkspace and CLI code stay
in the `net10.0` host. Compiler references only `GLSH.Abstractions`, while
DVG.Maths is referenced by fixtures/tests rather than compiler core.

The GLSL backend now emits valid array declarators and `void main()`, uses a
central deterministic identifier mangler for GLSL/Vulkan keywords, `gl_`
prefixes, generated members, and post-mangle collisions, and is covered by
golden tests. CLI check/emit use the implemented subset. The Vulkan test runs
`glslangValidator -V --target-env vulkan1.2 -S comp` followed by
`spirv-val --target-env vulkan1.2`; both pass on the reviewed machine.

The remaining 0.1 blockers are intentional scope, not hidden failures: the
current GLSL body is still a stage stub, and there is no manifest/reflection or
Silk.NET dispatch path yet.

Storage ABI is now explicitly canonical `std430`. The IR/manifest layout model
records offset, alignment, size, ArrayStride, and nullable MatrixStride, and
GLSL generation cannot select `scalarBlockLayout`. `vec3`/`float3` padding is
documented as a host-side ABI mismatch rather than hidden behind a second layout
or general-purpose runtime abstraction.
