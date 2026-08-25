# DeltaShader

DeltaShader validates a static C# shader subset with Roslyn, lowers it through
a typed IR and emits Vulkan GLSL 460. The build tool can compile that GLSL to
SPIR-V with `glslangValidator` and validate it with `spirv-val`.

```text
C# authoring source
  -> Roslyn validation and typed IR
  -> optional GLSL 460 inspection output
  -> SPIR-V compilation and validation
  -> ShaderArtifact { SPIR-V + binary ABI }
  -> DeltaRender
```

These are separate boundaries. Roslyn symbols, typed IR, GLSL, generated C#
helpers and CLI JSON are authoring/compiler tooling. Only the immutable
`Delta.Shader.Contract.ShaderArtifact` is the final runtime handoff. See the
[final artifact contract](docs/final-artifact-contract.md).

## Implemented authoring and compiler tooling

Shaders are compile-time static methods in an authoring project that references
`Delta.Shader.Abstractions`, `Delta.Maths` and the analyzer:

```csharp
[DeltaCompute(localSizeX: 64)]
public static void Compute(
    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<uint> input,
    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<uint> output,
    [GlobalInvocationId] uint id)
{
    if (id < input.Length)
        output[id] = input[id] * 2u + 1u;
}
```

The supported subset includes scalar/vector arithmetic, locals, conditionals,
structured loops, static helpers, std430 buffers, push and specialization
constants, stage builtins and supported `Delta.Maths` symbols. Reference types,
managed captures, allocation, exceptions, async, reflection, dynamic,
recursion and virtual/interface dispatch are analyzer errors.

Runtime values are explicit resources or constants, not closure captures.
Arbitrary runtime lambda, delegate or expression-tree transpilation is not
implemented; production authoring uses static attributed methods.

Maths mapping comes from the generated `shader-contract.json` and Roslyn symbol
identity, never CLR-name guesses. Source entry names remain compiler metadata;
the Vulkan entry point is currently emitted as `main`.

The CLI writes build-side `.glsl` and `.shader.json` files and, for the SPIR-V
backend, a validated `.spv` file. GLSL is for inspection and external
compilation. The JSON describes the current compiler compatibility manifest.
Neither file is an additional renderer contract.

## Final runtime handoff

`src/Delta.Shader.Contract` is the source of truth for the final artifact and
binary ABI. Its `ShaderArtifact` contains only:

- a format version, stage and entry point;
- owned SPIR-V bytes exposed as a read-only span, with an explicit copy for
  upload;
- a concrete `ShaderAbi` describing resource bindings, access and stage masks,
  push constants, stage/vertex interfaces, specialization constants, compute
  workgroup size, required capabilities and binary layouts.

The ABI layout records concrete size, alignment, member offset, array stride
and matrix stride values. At this boundary the original C# types have already
been erased. The artifact contains no content hash, `System.Type`, generic
resource object, delegate, reflection object, Roslyn state, compiler IR or GLSL
text. A consumer may compute its own cache key when importing it.

`Delta.Shader.Contract.GraphicsShaderProgram` is only a validated vertex and
fragment artifact pair. It adds no source-language or intermediate data.
DeltaRender binds packed bytes and renderer resource handles according to the
artifact ABI; it does not inspect the original CLR object graph.

Storage layout is std430. Storage `bool` occupies four bytes, and values such
as CLR `float3[]` must be packed from the artifact's concrete ABI rather than
uploaded using CLR layout assumptions.

## Migration state

The compiler, source generators, CLI, text factories and their tests still use
the older `ShaderArtifact`, `ShaderAbiManifest` and `GraphicsShaderProgram`
types in `Delta.Shader.Abstractions`. Those types and generated JSON are a
temporary compatibility surface, not a second final contract.

Producer migration must convert the fully resolved compiler result to
`Delta.Shader.Contract.ShaderArtifact` and erase compiler-only metadata before
runtime handoff. Consumers must migrate to that same contract. After both sides
migrate, the duplicate compatibility artifact types can be removed.

## Reusable text shader tooling

`src/Delta.Shader.Text` contains static C# authoring sources for SDF and MSDF
text shaders. Its generated factories expose GLSL and compatibility manifest
JSON and accept externally compiled SPIR-V. The factories currently return
`Delta.Shader.Abstractions.GraphicsShaderProgram`; they do not yet publish the
canonical final `Delta.Shader.Contract.GraphicsShaderProgram`.

The preparation script is a build/package step, never runtime compilation:

```bash
out_dir="$(mktemp -d)"
./eng/prepare-text-artifacts.sh "$out_dir"
```

It requires `glslangValidator`, `spirv-val` and `jq`, and writes matching
`.glsl`, `.spv` and `.shader.json` files for the SDF and MSDF vertex/fragment
pairs. The GLSL and JSON remain inspection and compatibility packaging outputs.
They must not be passed to DeltaRender as an alternative to the canonical
artifact.

The shader ABI currently represented by those compatibility manifests uses a
readonly std430 `GlyphInstance[]` at set 0/binding 0 with stride 48 and member
offsets `PixelMin=0`, `PixelMax=8`, `UvRect=16`, `Color=32`, plus
`TextParameters` push constants of size 64. The SDF fragment uses atlas sampler
set 0/binding 3; the MSDF fragment uses set 0/binding 4. Consumers must take
these values from the resolved ABI during migration rather than duplicate them.
The factories own no Vulkan, texture or atlas resources.

See [WORKFLOW.md](WORKFLOW.md) for bounded build and validation commands,
[docs/diagnostics.md](docs/diagnostics.md) for diagnostics,
[TRANSFORM_CONFORMANCE.md](TRANSFORM_CONFORMANCE.md) for matrix/layout
conformance and [TODO.md](TODO.md) for selected work. Start agent work at
[AGENTS.md](AGENTS.md).
