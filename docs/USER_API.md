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

Every shader-visible field must explicitly declare its role as an interstage
payload, storage buffer, texture, push constant, or stage builtin. The compiler
flattens those fields into resource and layout metadata. Nested managed state,
reference fields, and arbitrary host services remain invalid.

### Generated std430 packing

The analyzer emits typed packing methods on the generated artifact factory.
For an entry point named `Compute`, the generated surface includes methods in
this shape:

```csharp
int PackComputeContext(in ComputeParametersContext value, Span<byte> destination);
byte[] PackComputeContext(in ComputeParametersContext value);
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

For a program with multiple storage-buffer resources, the generated surface also
exposes `<Method>StorageBufferCount`,
`Get<Method>StorageBufferByteLength(int)` and
`Get<Method>StorageBufferRanges(int, Span<Delta.Shader.Packing.ShaderBufferRange>)`. The range plan
contains the resolved set, binding, offset, size and element stride. A host may
place all resource ranges in one backing allocation and call each generated
element packer on `Std430Packer.GetRange(backing, range)`. This reduces physical
buffer allocations; it does not merge descriptor bindings or change the final
`ShaderAbi`.

### Generated vertex-buffer packing

Vertex data uses the same generated packing path as storage-buffer elements;
it is not a second hand-written ABI. The canonical producer source is the
`DeltaShader.Mesh` project at
`src/DeltaShader.Mesh/DeltaShader.Mesh.csproj`. A consumer references that
project and gets the generated public
`Delta.Shader.Mesh.MeshShadersGraphicsShaderProgram` type. Its `Mesh` vertex
entry point has a payload containing `[Layout(0)] Position Position`,
`[Layout(1)] WorldNormal Normal` and `[Layout(2)] Uv0 Uv`, so the generated
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
For an artifact with multiple resolved vertex bindings, the generated type also
exposes `PackMeshVertexBinding<binding>Element/Elements` methods for each
binding. The single-binding-0 case keeps the shorter `PackMeshVertexElement`
names. The same generated type exposes `FragmentAbi` and
`CreateProgram(ReadOnlySpan<byte>, ReadOnlySpan<byte>)`; the SPIR-V bytes come
from the explicit DeltaShader Tool/package step, not from this authoring
assembly. Each binding helper uses its own resolved `VertexInputs` subset and
`VertexBuffers[binding].Stride`; consumers may place those ranges in one
persistent backing allocation when their Render adapter supports aliased
bindings.
The generated surface also exposes `<Method>VertexBufferCount`,
`Get<Method>VertexBufferByteLength(int)` and
`Get<Method>VertexBufferRanges(int, Span<Delta.Shader.Packing.ShaderBufferRange>)`
for allocating one backing vertex buffer while retaining the ABI's binding
indices.

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
ordinary context fields, while one interstage field contains the stage-data
payload. The canonical payload uses semantic value types. `Position` is the
required vertex position semantic; `Uv0`, `Color`, `VertexColor`, and
`FragmentColor` carry their meaning in their full type identity rather than in
the CLR field name. Direct scalar/vector fields are rejected; migrate the field
to the `Delta.Shader.Position` semantic type.

```csharp
[Interstage]
public struct InterstageData
{
    [Layout(0)]
    public Position Position;

    [Layout(1)]
    public VertexColor VertexColor;

    public Uv0 Uv;
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
    return context.Vertex.VertexColor.Value;
}
```

`[Layout(location)]` inside the vertex payload describes the physical vertex
buffer location for a host-provided value. Semantic fields without a location
are stage outputs and receive an interstage location assigned by the compiler.
When the same semantic payload is consumed by the fragment stage, the compiler
matches the full semantic type identity and emits matching GLSL
`layout(location = N) in/out` declarations. Users do not write those
interstage locations.

An interstage payload may contain nested user-defined value structs. The
compiler recursively flattens those structs in declaration order until it
reaches semantic leaves; only semantic types such as `Position`, `Uv0` and
`VertexColor` may cross the stage boundary. Nested container fields are not
physical interface variables. A repeated leaf field symbol or an unwrapped
mapped type such as `float2` is rejected before lowering.

`Position` is stage-aware. In the vertex output it lowers to `gl_Position`.
The `Delta.Shader.Position` semantic type defines the vertex position and is
lowered to `gl_Position`. Fragment/window position is a different semantic and
is never silently introduced as a builtin. The compiler
rejects a payload without the required `Position` semantic or with an
incompatible position declaration.

The vertex payload is intentionally one structure rather than a separate
`VertexOutput` wrapper. Its fields describe the data that crosses the stage
boundary, while the surrounding context keeps resources and constants
separate from that data.

## Composite shaders

Typed semantic payloads are part of the current graphics authoring surface.
The editor workflow may select ordered vertex and fragment layers, then
DeltaShader emits one final artifact and ABI. Semantic fields are matched by
their full semantic type identity, not by field name; omitted layer fields are
forwarded unchanged and unused fields are removed from the physical interface.
The composition boundary is in [shader-composition.md](shader-composition.md).

Composition is compile-time/tooling work, not runtime C# execution or several
Vulkan entry points per draw. The current compiler still emits one static
vertex and one static fragment entry point per generated pair.

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
  --profile vulkan1.2 --spirv 1.5 --glsl 460 \
  --optimize performance \
  --out ./src/DeltaShader/CompiledShaders
```

The command requires `glslangValidator` and `spirv-val` for the SPIR-V backend.
Runtime consumption, resource creation, descriptor binding, and dispatch are
defined by the producer-owned [CONTRACT.md](CONTRACT.md), not by this authoring
API.
### Shared backing buffer

Generated range helpers describe logical storage resources inside one backing
buffer. Allocate that buffer from the resolved plan instead of calculating
offsets in the consumer:

```csharp
Span<ShaderBufferRange> ranges = stackalloc ShaderBufferRange[Program.StorageBufferCount];
Program.GetProgramStorageBufferRanges(elementCount, ranges);
var backing = new byte[Std430Packer.GetBackingByteLength(ranges)];

var resourceBytes = Std430Packer.GetRange(backing, ranges[0]);
Program.PackProgramInputElements(values, resourceBytes);
```

The range plan owns offsets, sizes, alignment, and element strides. Multiple
logical resources may therefore share one physical backing buffer without
consumer-side std430 arithmetic.

Graphics programs also expose `GetProgramSharedBufferRanges` and
`GetProgramSharedBufferByteLength` when they have vertex inputs. The shared plan
lists storage-buffer ranges first and vertex-buffer ranges second, all using one
non-overlapping offset space. `Program.SharedBufferRangeCount` gives the total
entry count; the generated ABI remains the source of truth for both groups.
