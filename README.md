# DeltaShader

Roslyn-based compiler for a validated C# shader subset targeting Vulkan. It
emits readable GLSL 460, validated SPIR-V and a versioned runtime-neutral ABI
manifest for compute, vertex and fragment stages.

```text
C# project -> Roslyn symbols/IOperation -> typed IR -> GLSL 460
  -> glslangValidator -> SPIR-V -> spirv-val -> ShaderArtifact
```

`Delta.Shader.Contract` owns the final `ShaderArtifact`, graphics-program
composition and binary ABI. It depends on neither Roslyn, GLSL nor Vulkan.
`Delta.Shader.Abstractions` continues to own authoring attributes and resource
wrappers; its older artifact/dispatch surface is a compatibility layer until
the compiler and DeltaRender migrate to the contract project. DeltaRender must
not define another manifest or graphics-program type.

The renderer handoff is specifically the immutable final artifact: validated
SPIR-V plus its serialized binary ABI. It contains no content hash, live CLR
generic values, Roslyn state, typed IR or GLSL. See the
[final artifact contract](docs/final-artifact-contract.md). GLSL and generated
typed helpers are build/authoring outputs, not renderer inputs.

Closed resource categories use `ShaderResourceKind` inside the compiler and
IR. The serialized `ShaderAbiResource.Category` field keeps its legacy
`storage-buffer`/`sampled-texture` wire names for consumer compatibility;
adapter code can map it with `ShaderResourceKindExtensions`, which returns
`Unknown` for future values. GLSL names, CLR symbol names and profile/version
strings remain strings because they are extensible identifiers.

Storage and shared structures use std430. The manifest is authoritative for
offset, alignment, size, array stride and matrix stride. Storage `bool` is four
bytes; CLR `float3[]` must be packed according to the manifest rather than
uploaded as a tightly packed GLSL `vec3[]`.

## Authoring contract

Shaders are compile-time static methods in a small authoring project that
references only abstractions and Maths:

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

Supported code includes scalar/vector arithmetic, locals, conditionals,
structured loops, static helpers, std430 buffers, push/specialization constants,
stage builtins and supported `Delta.Maths` symbols. Reference types, managed
captures, allocation, exceptions, async, reflection, dynamic, recursion and
virtual/interface dispatch are analyzer errors.

Runtime values are explicit resources or constants, not implicit closure
captures. The source generator emits typed artifact wrappers; the CLI publishes
`.glsl`, `.spv` and `.shader.json` files so consumers need no Roslyn/MSBuild.
Arbitrary runtime lambda compilation is not implemented: a `Delegate` or
expression tree is never transpiled at runtime. Use a static `[DeltaCompute]`
method in the compile-time authoring project.

## Reusable text artifacts

Reference `src/Delta.Shader.Text/Delta.Shader.Text.csproj` together with
`Delta.Maths` and the Delta.Shader analyzer. The project owns the static C#
authoring source and the generator publishes two factories:

```csharp
var sdfVertexSpirv = File.ReadAllBytes("SdfTextVertex.vert.spv");
var sdfFragmentSpirv = File.ReadAllBytes("SdfTextFragment.frag.spv");
var msdfVertexSpirv = File.ReadAllBytes("MsdfTextVertex.vert.spv");
var msdfFragmentSpirv = File.ReadAllBytes("MsdfTextFragment.frag.spv");

var sdf = SdfTextGraphicsShaderProgram.CreateProgram(sdfVertexSpirv, sdfFragmentSpirv);
var msdf = MsdfTextGraphicsShaderProgram.CreateProgram(msdfVertexSpirv, msdfFragmentSpirv);
```

Each factory also exposes generated `VertexGlsl`, `FragmentGlsl`,
`VertexManifestJson` and `FragmentManifestJson`; SPIR-V is supplied by the
explicit CLI/tool build step, not checked in. Both programs use the same
immutable ABI: a readonly std430 `GlyphInstance[]` at set 0/binding 0 with
stride 48 and member offsets `PixelMin=0`, `PixelMax=8`, `UvRect=16`,
`Color=32`, plus `TextParameters` push constants of size 64. The SDF fragment
uses atlas sampler set 0/binding 3; the MSDF fragment uses set 0/binding 4.
The atlas is a readonly combined `sampler2D`; the vertex stage emits six
vertices per instance from `VertexIndex` and reads `InstanceIndex`. Glyph pixel
coordinates use the canonical top-left UI convention: `clip.y = 1 -
pixel.y / resolution.y * 2`; renderer viewport state remains separate.
Consumers
must use the manifest for descriptor and push-constant layout rather than
duplicating these values. The generated factories use their embedded manifests
and accept only the two stage SPIR-V byte arrays; the distributed JSON files
remain available for ABI inspection and packaging validation, but are not
deserialized again by this factory path. The factories are shader artifacts
only and do not own Vulkan, texture or atlas resources.

### Preparing distributable text artifacts

The producer preparation step builds the tool and text authoring project, then
emits the generated GLSL, SPIR-V and manifests with stable entry-point names.
It requires `glslangValidator`, `spirv-val` and `jq`; missing tools fail with
an explicit diagnostic. The command performs compilation only during this
preparation step, never at runtime, and does not check in generated files:

```bash
out_dir="$(mktemp -d)"
./eng/prepare-text-artifacts.sh "$out_dir"
```

The SDF files are `SdfTextVertex.vert.{glsl,spv,shader.json}` and
`SdfTextFragment.frag.{glsl,spv,shader.json}`. The same directory also receives
the corresponding `MsdfTextVertex` and `MsdfTextFragment` files. Consumers
load the two `.spv` files for the selected pair and pass only those bytes to
the generated factory. The matching `.shader.json` remains the distributive
ABI artifact for descriptor/push-constant setup and validation; no ABI values
are duplicated in the consumer.

Compute shaders may declare a `[SampledTexture2D(set, binding, ShaderStageMask.Compute)]`
resource and sample it through `ShaderIntrinsics.SampleCompute<TCoordinate, TColor>`.
Generic storage buffers expose indexed access (`buffer[index]`) as the canonical
authoring form; `Load`/`Store` remain compatibility members. Texture resources are
opaque combined samplers supplied by the runtime and do not expose CPU storage.

Maths mapping comes only from its generated `shader-contract.json` and Roslyn
symbol identity. Source entry names stay in metadata while Vulkan entry points
are currently emitted as `main`.

See [WORKFLOW.md](WORKFLOW.md) for CLI/build/validation commands,
[docs/diagnostics.md](docs/diagnostics.md) for diagnostics,
[TRANSFORM_CONFORMANCE.md](TRANSFORM_CONFORMANCE.md) for CPU/GPU transforms,
and [TODO.md](TODO.md) for selected work. Start agent work at
[AGENTS.md](AGENTS.md).
