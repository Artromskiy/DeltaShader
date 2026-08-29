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

Every entry point takes one user-defined shader-visible context value as its
`in` parameter. A context is an ordinary user-defined `readonly struct`; it is
not an array type and does not inherit from a framework base class. A context
contains descriptors, push constants, and, for graphics stages, one structured
stage-data field.

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

### Generated std430 packing

The analyzer emits typed packing methods on the generated artifact factory.
For an entry point named `Compute`, the generated surface includes methods in
this shape:

```csharp
int PackComputeContext(in ComputeContext value, Span<byte> destination);
byte[] PackComputeContext(in ComputeContext value);
int PackComputeInputElement(in uint value, Span<byte> destination);
int PackComputeInputElements(ReadOnlySpan<uint> values, Span<byte> destination);
byte[] PackComputeInputElements(ReadOnlySpan<uint> values);
```

The exact resource type and method names come from the shader symbols and
resolved manifest. Methods write explicit std430 offsets and clear the packed
range before writing, so ordinary CLR sequential layout is never uploaded as
a substitute. `float3`, matrices and nested shader structs are written
component-by-component using the resolved padding and column-major matrix
strides. DeltaRender receives these bytes and owns the actual GPU upload.

For readback, the generated factory also exposes typed methods such as
`UnpackComputeInputElement(ReadOnlySpan<byte>)` and
`UnpackComputeInputElements(ReadOnlySpan<byte>)`. They return user values after
reading the resolved offsets and array stride. Unpack is emitted only for
writable value payloads; a context containing descriptors is not reconstructed
from bytes.

### Generated vertex-buffer packing

Vertex data uses the same generated packing path as storage-buffer elements;
it is not a second hand-written ABI. The canonical producer source is the
`DeltaShader.Mesh` project at
`src/DeltaShader.Mesh/DeltaShader.Mesh.csproj`. A consumer references that
project and gets the generated public
`Delta.Shader.Mesh.MeshShadersGraphicsShaderProgram` type. Its `Mesh` vertex
entry point has a payload containing `[Position] float4 Position`,
`[Layout(1)] float3 Normal` and `[Layout(2)] float2 Uv`, so the generated
surface is:

```csharp
int PackMeshVertexElement(in MeshPayload value, Span<byte> destination);
byte[] PackMeshVertexElement(in MeshPayload value);
int PackMeshVertexElements(ReadOnlySpan<MeshPayload> values, Span<byte> destination);
byte[] PackMeshVertexElements(ReadOnlySpan<MeshPayload> values);
```

The element helper writes one interleaved vertex using the resolved
`ShaderAbi.VertexInputs` offsets. The array helper repeats the same operation
using the resolved binding stride, including padding between records. The
authoring project does not specify byte offsets or stride; `[Layout(location)]`
declares vertex locations, while the compiler resolves the physical layout.
Render uploads the returned bytes as a vertex buffer and uses the same
`MeshShadersGraphicsShaderProgram.VertexAbi` to create the vertex-input state.
The same generated type exposes `FragmentAbi` and
`CreateProgram(ReadOnlySpan<byte>, ReadOnlySpan<byte>)`; the SPIR-V bytes come
from the explicit DeltaShader Tool/package step, not from this authoring
assembly. The current generated surface supports the resolved binding-0
stream; multiple vertex-buffer bindings are a separate compiler feature.

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
ordinary context fields, while one `[Interstage]` field contains the stage-data
payload. The payload must contain one explicit `float4` `[Position]` field.

```csharp
[Interstage]
public struct InterstageData
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
    [Interstage]
    public readonly InterstageData Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<int> Values;
}

public readonly struct FragmentContext
{
    [Interstage]
    public readonly InterstageData Vertex;

    [Layout(0, 1)]
    public readonly ReadOnlyStorageBuffer<float> OtherValues;
}

[VertexShader]
public static InterstageData VertexEntry(in VertexContext context)
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

The entry-point contract requires exactly one `in` shader context parameter;
resources use `[Layout(set, binding)]`, and execution builtins are accessed
through `ShaderBuiltins`.

The analyzer rejects reference types, managed state, captures, allocation,
reflection, virtual/interface dispatch and other unsupported CLR constructs.
Arbitrary runtime lambdas, delegates and expression-tree transpilation are not
part of this API. The CLR method is authoring input; it is never invoked by
the GPU.

### SDF/MSDF text parameters

`Delta.Shader.Text` provides `SdfTextVertex`/`SdfTextFragment` and
`MsdfTextVertex`/`MsdfTextFragment`. Their `TextParameters` push-constant
payload uses these units:

- `DistanceRange` is the positive signed-distance range represented by the
  texture's encoded `[0, 1]` span, measured in atlas distance-field units.
- `OutlineWidth` is the outer outline width in the same distance-field units.
  The host converts any UI or logical-pixel width before packing the value.
- Positive signed distance is inside the glyph. Fill coverage increases with
  signed distance; outline coverage is the finite band outside the contour and
  is zero when `OutlineWidth` is zero.

`DistanceRange` must be positive. The SDF path uses the texture alpha channel;
the MSDF path uses the median of RGB. Both paths use `fwidth` for analytic
anti-aliasing and expose `TextColor`/`OutlineColor` explicitly in the same
push-constant block.

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
