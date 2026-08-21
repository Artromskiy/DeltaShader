# DeltaShader

Roslyn-based compiler for a validated C# shader subset targeting Vulkan. It
emits readable GLSL 460, validated SPIR-V and a versioned runtime-neutral ABI
manifest. Compute, vertex and fragment stages are supported.

## Pipeline and boundaries

```text
C# project
  -> Roslyn symbols + IOperation
  -> DeltaShader validation and typed IR
  -> GLSL 460
  -> glslangValidator
  -> SPIR-V
  -> spirv-val
  -> ShaderArtifact { Spirv, Stage, EntryPoint, Manifest }
```

`Delta.Shader.Abstractions` contains attributes, resource wrappers,
`ShaderArtifact`, ABI metadata and compute-dispatch contracts. It does not
depend on Roslyn, Vulkan, or DeltaRender. DeltaRender consumes artifacts and
must not define a second shader manifest.

Storage/shared structures use std430. Manifest metadata is authoritative for
offset, alignment, size, array stride and matrix stride. CLR `float3` must not
be uploaded as a tightly packed GLSL `vec3` array; use the manifest packing
plan. Storage `bool` is represented as a four-byte value.

## Authoring

```csharp
using Delta.Shader.Abstractions;

public static class Doubler
{
    [DeltaCompute(localSizeX: 64)]
    public static void Compute(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<uint> input,
        [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<uint> output,
        [GlobalInvocationId] uint invocation)
    {
        if (invocation < input.Length)
            output[invocation] = input[invocation] * 2u + 1u;
    }
}
```

Shader authoring projects should reference only abstractions and Maths. Do not
make the project opened by `MSBuildWorkspace` depend on renderer, Vulkan, SDL,
or an executable host.

Supported language includes scalar/vector arithmetic, comparisons, locals,
conditionals, structured loops, static helpers, std430 storage buffers, push
constants, specialization constants, stage builtins and supported
`Delta.Maths` symbols. Reference types, allocation, exceptions, async,
reflection, dynamic, recursion and virtual dispatch are rejected with `DSHxxx`
diagnostics.

Delta.Maths symbol and layout mapping comes only from its generated
`shader-contract.json`. Register `Builtin` and `Helper` identities; never infer
GLSL semantics from CLR names or register `Unsupported` aliases.

## Compile-time typed kernels

The build-time shader contract is a static partial method (or static method in
the CLI fixture) annotated with `[DeltaCompute]`. Its parameter types and
resource attributes are analyzed by `Delta.Shader.Analyzers` during Roslyn
compilation, then the existing compiler and CLI produce GLSL 460, SPIR-V and
the ABI manifest:

```csharp
[DeltaCompute(localSizeX: 64)]
public static void Compute(
    [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<float> input,
    [ReadWriteStorageBuffer(0, 1)] ReadWriteStorageBuffer<float> output,
    [GlobalInvocationId] uint id)
{
    output[id] = maths.sin(input[id]);
}
```

The source generator also emits a typed `<ContainingType><Method>ShaderArtifact`
wrapper with GLSL, manifest JSON and `CreateArtifact(byte[] spirv)`. The CLI
artifact (`.spv` plus `.shader.json`) is the runtime-neutral distribution form;
it does not require Roslyn, MSBuild or Vulkan bindings in the consumer. For
graphics stages the CLI writes stage-qualified names such as
`sdf-text.vert.glsl`, `sdf-text.frag.glsl`, `msdf-text.vert.glsl` and
`msdf-text.frag.glsl`.

Compile-time constants and local value variables are allowed. Managed closure
state, runtime captures, reference values, reflection and virtual/interface
calls are rejected with diagnostics; a runtime value must be an explicit
resource or a future push-constant parameter rather than an implicit capture.

Build the typed fixture with:

```bash
dotnet run --project src/Delta.Shader.Tool/Delta.Shader.Tool.csproj \
  -c Release --no-build -- build \
  tests/Delta.Shader.TestShaders/Delta.Shader.TestShaders.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out <directory>
```

The analyzer rejects managed mutable state, reference locals, reflection and
virtual/interface calls with DSH014. Shader-visible reference types remain
DSH010 errors. Static methods are the compile-time shader contract and support
assignments, locals and indexed resources directly.

## CLI

```bash
dotnet run --project src/Delta.Shader.Tool/Delta.Shader.Tool.csproj \
  -c Release --no-build -- \
  check <shader-project.csproj>

dotnet run --project src/Delta.Shader.Tool/Delta.Shader.Tool.csproj \
  -c Release --no-build -- \
  build <shader-project.csproj> --profile vulkan1.2 --spirv 1.5 \
  --glsl 460 --out <directory>
```

`build` writes `<entry>.glsl`, `<entry>.spv` and `<entry>.shader.json`; SPIR-V
is published only after both validators succeed. Graphics stages use
stage-qualified filenames so a vertex/fragment pair becomes
`<stem>.vert.glsl`, `<stem>.frag.glsl`, `<stem>.vert.spv`, `<stem>.frag.spv`
and matching manifest files. Source entry-point names stay in metadata while
Vulkan entry points are currently emitted as `main`.

For the current viewport/cube contract, Rend consumes the manifest without
duplicating ABI rules:

- vertex inputs: location 0 `vec3 position` (`VK_FORMAT_R32G32B32_SFLOAT`),
  location 1 `vec3 normal` (`VK_FORMAT_R32G32B32_SFLOAT`), location 2 `vec2 uv`
  (`VK_FORMAT_R32G32_SFLOAT`)
- vertex buffer binding: binding 0, stride 32, input rate vertex
- transform/light scene data: readonly std430 storage buffer at set 0 binding 0
  with `Model`, `View`, `Projection`, `LightDirection`, `LightColor` and the
  manifest offsets/alignment/size values
- sampled texture: combined sampler at set 0 binding 1
- draw call shape: a vertex-layout-compatible cube mesh; 36 vertices is the
  simplest drop-in path

This keeps the vertex layout, buffer offsets and resource bindings single-sourced
in the shader artifact manifest.

## Verify

From this repository:

```bash
dotnet build DeltaShader.slnx -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
dotnet test DeltaShader.slnx -c Release --no-build --no-restore \
  --disable-build-servers -m:1
```

Real C# shader → Vulkan compute verification is owned by DeltaRender:

```bash
cd ../DeltaRender
./tools/run-delta-shader-compute-smoke.sh
```

On macOS that script restores/builds `osx-arm64`, loads MoltenVK, dispatches the
shader and verifies GPU readback. Common IPC, RID and native-loader failures are
documented in the workspace `README.md`. Benchmarks are manual only.

## Active UI shader milestone

The next bounded compiler feature is a truthful sampled-texture route for the
editor text pipeline: texture/sampler declarations, stage validation, typed
IR, GLSL 460 lowering, manifest metadata and SPIR-V validation. On top of that
route, provide generated C# SDF and MSDF fragment shaders. MSDF coverage uses
the median RGB distance with derivative-based anti-aliasing; fixed smoothing
widths tied to one resolution are not accepted.

The current route uses a Vulkan combined image sampler contract:

    [FragmentShader]
    public static void Text(
        [SampledTexture2D(0, 3)] SampledTexture2D atlas,
        [FragmentCoord] float2 pixel,
        [FragmentColor] out float4 color)
    {
        var msdf = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, pixel);
        var median = maths.max(maths.min(msdf.x, msdf.y),
            maths.min(maths.max(msdf.x, msdf.y), msdf.z));
        var edge = ShaderIntrinsics.fwidth(median - 0.5f);
        color = new float4(1f - maths.smoothStep(-edge, edge, median - 0.5f));
    }

The generated GLSL declaration is layout(set = 0, binding = 3) uniform
sampler2D ...;. The versioned manifest records the resource stage, set,
binding, sampler2D type, Layout = opaque and packing Scheme = none; there are
deliberately no std430 offsets or strides for this resource. SampledTexture2D
is valid in vertex and fragment stages only, and
ShaderIntrinsics.SampleVertex/SampleFragment are resolved by Roslyn symbol
identity. fwidth is fragment-only. Texture image ownership, descriptors and
sampler creation remain runtime responsibilities outside Delta.Shader.

tests/Delta.Shader.TestShaders/GeneratedTextShaders.cs contains the bounded
SDF and MSDF authoring examples as matching vertex/fragment pairs. The MSDF
path uses median RGB distance, derivative-based smoothing, and explicit
std430-compatible push-constant color and outline parameters. Runtime lambdas
and captures are not part of this compile-time static contract.

DeltaShader owns shader-visible contracts and compilation only. DeltaRender
owns Vulkan images/descriptors/atlases, while DeltaXAML owns text/control
semantics. Acceptance and ownership are tracked in
[`../EDITOR_UI_TODO.md`](../EDITOR_UI_TODO.md), P6.
