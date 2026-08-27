# DeltaShader user API

This is the user-facing C# shader authoring API. It does not define the
cross-project runtime artifact contract; see [CONTRACT.md](CONTRACT.md).

## Authoring

An authoring project references:

- `src/DeltaShader/DeltaShader.csproj` for shader
  attributes, builtins and resource declarations;
- `src/DeltaShader.Analyzers/DeltaShader.Analyzers.csproj` as an analyzer;
- `DeltaMaths` when using supported DeltaMaths shader symbols.

### User-defined context contract

The selected authoring contract is one user-defined shader-visible value
context instead of a long list of entry-point parameters. A context is an
ordinary user-defined `readonly struct`; it is not an array type and does not
inherit from a framework base class. A context contains descriptors, push
constants, and, for graphics stages, one structured stage-data field.

```csharp
public readonly struct ComputeParametersContext
{
    [PushConstant]
    public readonly uint Count;

}

[ComputeShader(localSizeX: 64)]
public static void Compute(in ComputeParametersContext ctx)
{
    uint id = ShaderBuiltins.GlobalInvocationId.X;
    if (id >= ctx.Count)
        return;

    // Use id and other context fields in the shader body.
}
```

Every shader-visible field must be explicitly role-annotated as a storage
buffer, texture, push constant, or stage builtin. The compiler flattens those
fields into resource and layout metadata. Nested managed state, reference
fields, and arbitrary host services remain invalid.

Shader execution builtins are static compiler intrinsics, not context fields:

```csharp
uint id = ShaderBuiltins.GlobalInvocationId.X;
uint vertex = ShaderBuiltins.VertexIndex;
uint instance = ShaderBuiltins.InstanceIndex;
float depth = ShaderBuiltins.FragmentCoord.Z;
```

Their placeholder CLR accessors are never executed. The compiler resolves each
property by symbol identity and validates its stage before emitting the
corresponding GLSL builtin.

`[Layout(set, binding)]` is the common descriptor-binding form for storage
buffers, textures, and samplers. The resource type supplies its access
contract and shader kind; the attribute supplies only descriptor coordinates.
The one-argument `[Layout(location)]` form is reserved for vertex inputs.

For example, a context can contain buffers as well as constants:

```csharp
public readonly struct BufferComputeContext
{
    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<uint> Input;

    [Layout(0, 1)]
    public readonly ReadWriteStorageBuffer<uint> Output;

    [PushConstant]
    public readonly uint Count;

    public BufferComputeContext(
        ReadOnlyStorageBuffer<uint> input,
        ReadWriteStorageBuffer<uint> output,
        uint count)
    {
        Input = input;
        Output = output;
        Count = count;
    }
}

[ComputeShader(localSizeX: 64)]
public static void Compute(in BufferComputeContext ctx)
{
    uint id = ShaderBuiltins.GlobalInvocationId.X;
    if (id < ctx.Count)
        ctx.Output[id] = ctx.Input[id] * 2u + 1u;
}
```

### Graphics context contract

Graphics contexts use the same shape: descriptors and push constants remain
ordinary context fields, while one `[Varying]` field contains the stage-data
payload. The payload must contain one explicit `float4` `[Position]` field.

```csharp
[Varying]
public struct VaryingStruct
{
    [Position]
    [Layout(0)]
    public float4 Position;

    [Layout(1)]
    public float3 Color;

    [Layout(2)]
    public float2 Uv;
}

public readonly struct VertexContext
{
    [Varying]
    public readonly VaryingStruct Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<int> Values;
}

public readonly struct FragmentContext
{
    [Varying]
    public readonly VaryingStruct Vertex;

    [Layout(0, 1)]
    public readonly ReadOnlyStorageBuffer<float> OtherValues;
}

[VertexShader]
public static VaryingStruct VertexEntry(in VertexContext context)
{
    return context.Vertex;
}

[FragmentShader]
public static float4 FragmentEntry(in FragmentContext context)
{
    return new float4(context.Vertex.Color, 1.0f);
}
```

`[Layout(location)]` inside the vertex payload describes the physical vertex
buffer locations. When the same payload is consumed by the fragment stage,
the compiler automatically assigns interstage locations and emits matching
GLSL `layout(location = N) in/out` declarations. Users do not write those
interstage locations.

`[Position]` is stage-aware. In the vertex output it lowers to `gl_Position`.
If a fragment shader needs the vertex clip position, it must also carry it as
a separate ordinary varying field. Fragment/window position is a different
semantic and must be explicitly requested; it is never silently introduced as
a builtin. The compiler must reject a payload without the required
`[Position] float4` field or with an incompatible position declaration.

The vertex payload is intentionally one structure rather than a separate
`VertexOutput` wrapper. Its fields describe the data that crosses the stage
boundary, while the surrounding context keeps resources and constants
separate from that data.

Compile-time `const` values remain inlined and do not become context fields or
bindings. Values supplied by host code must be explicitly annotated as push
constants or declared resources.

Host code may wrap the same value contract in an ordinary object and call a
generated typed dispatch wrapper:

```csharp
var context = new BufferComputeContext(input, output, count);
await shader.DispatchAsync(context);
```

This does not transfer a CLR object to the GPU. The generated wrapper extracts
the declared buffers and constants, while the renderer-owned adapter creates
the actual GPU dispatch. A CPU execution helper may use the same context for
tests, but CPU and GPU remain separate execution backends.

The parameter-based resource and builtin forms are removed. A
`[ComputeShader]` method must have exactly one `in` shader context parameter;
resources use `[Layout(set, binding)]`, and execution builtins are accessed
through `ShaderBuiltins`.

The analyzer rejects reference types, managed state, captures, allocation,
reflection, virtual/interface dispatch and other unsupported CLR constructs.
Arbitrary runtime lambdas, delegates and expression-tree transpilation are not
part of this API. The CLR method is authoring input; it is never invoked by
the GPU.

## Build-side publication

The CLI emits GLSL and validates SPIR-V through the pinned target profile:

```bash
dotnet run --project src/DeltaShader.Tool/DeltaShader.Tool.csproj \
  -c Release -- build tests/DeltaShader.TestShaders/DeltaShader.TestShaders.csproj \
  --profile vulkan1.2 --spirv 1.5 --glsl 460 --out ./artifacts/shaders
```

The command requires `glslangValidator` and `spirv-val` for the SPIR-V backend.
Runtime consumption, resource creation, descriptor binding, and dispatch are
defined by the producer-owned [CONTRACT.md](CONTRACT.md), not by this authoring
API.
