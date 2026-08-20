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
it does not require Roslyn, MSBuild or Vulkan bindings in the consumer.

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
is published only after both validators succeed. Source entry-point names stay
in metadata while Vulkan entry points are currently emitted as `main`.

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
