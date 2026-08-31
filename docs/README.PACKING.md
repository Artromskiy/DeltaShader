# Generated packing boundary

This document defines how host values become bytes for a selected DeltaShader
artifact without making CLR layout, Vulkan layout, or an editor graph a second
contract. It is a design document for the packing boundary. It does not change
the frozen `ShaderAbi`/`ShaderArtifact` contract.

## Decision

The generated packer is a capability of a compiled shader program, not a base
class or an interface that every user value must implement.

The normal user path remains typed:

```csharp
await shader.DispatchAsync(context);
```

The generated dispatch wrapper calls the generated packers for the selected
artifact. A consumer that owns a lower-level upload path may call the same
generated methods explicitly:

```csharp
Program.PackFrameConstants(in frame, pushConstantBytes);
Program.PackInstances(instances, instanceBytes);
Program.PackVertexElements(vertices, vertexBytes);
```

The names above are illustrative. The generator derives the actual names from
the entry point, resource roots, vertex bindings, and resolved symbols.

The following rules are normative:

- User authoring values remain ordinary blittable or value-contract CLR types.
- Generated code is the only supported conversion from those values to bytes.
- Every generated method is derived from the same resolved `ShaderAbi` that the
  selected artifact exposes.
- `ShaderAbi` remains the only runtime description of offsets, alignment, size,
  array stride, matrix stride, bindings, and stage visibility.
- A packer never infers layout from `sizeof`, `Marshal.SizeOf`, CLR field order,
  `MemoryMarshal`, reflection, or a copied manifest.
- A packer is stateless and may be cached; creating one object per dispatch is
  not required.
- The generated code is host-side code. It is never visible to shader lowering
  and never executes on the GPU.

## Why the packer belongs to the artifact

One CLR type can be used by several shaders with different layouts. A value
type must therefore not implement a single permanent packer. For example, the
same `FrameConstants` type can have different live members or offsets in two
composites, and a vertex value can be used with different bindings or strides.

The relationship is therefore:

```text
CLR value type + selected generated program
    -> generated typed packer for that program
    -> bytes described by selected ShaderArtifact.Abi
```

The packer is not a third layout model. Its constants are generated from the
resolved ABI, and its public ABI accessor returns the same resolved ABI used by
the artifact factory. A dynamic host adapter may hold the generated packer and
the artifact together, but it must not copy or reinterpret layout fields.

## Two host lanes

The design deliberately has a fast typed lane and a dynamic selection lane.
They share the generated source and the final artifact; they differ only in
how the host selects the generated code.

### Typed lane

The typed lane is used by ordinary engine code, samples, tests, and user
scripts whose shader type is known at compile time.

- `DispatchAsync(in TContext)` accepts the original context value.
- Generated root helpers pack push-constant values without constructing a
  descriptor-bearing context workaround.
- Generated element helpers pack one SSBO or vertex value.
- Generated array helpers pack a contiguous range using the resolved stride.
- Generated unpack helpers decode writable payloads and readback values using
  the same offsets and strides.
- The compiler and C# type system reject unsupported fields before the packer
  exists.

No user code needs to mention `std430`, Vulkan offsets, descriptor handles, or
padding in this lane.

### Dynamic selection lane

The Editor and other tooling may select a precompiled composite at runtime.
This is dynamic artifact selection, not runtime shader compilation.

The editor performs the following bounded operation:

```text
selected layer symbols and order
    -> cached compiled composite identity
    -> generated program and generated packer adapter
    -> typed host values for the selected ports
    -> one render/dispatch command
```

The adapter is an erased host-only view over generated packer methods. It may
use a generated registration table or a host value bag for inspector-driven
values, but it must obey these boundaries:

- The adapter is selected by the compiled program identity, never by a short
  field name or a guessed CLR type.
- The adapter invokes generated typed code; it does not calculate offsets.
- Any dynamic type check happens at the editor/command-building boundary, not
  in shader code or in the Vulkan submission hot path.
- Once a command is built, its packer, ABI, and artifact are fixed together.
- A missing, stale, or incompatible value produces a clear editor/build
  diagnostic instead of a partially packed buffer.
- The adapter can be cached for the composite cache key and reused across
  frames.

## Composite host projection

The source contexts of a composite are not the runtime input type. They are
authoring declarations owned by individual layers. After layer selection and
liveness analysis, DeltaShader creates a host projection for the selected
composite. This projection is the link between the final shader and ordinary
CLR values.

For a statically known composite, the producer generates a concrete host type
and program facade. The shape is conceptually:

```csharp
public readonly struct CompositeParameters
{
    public readonly FrameConstants Frame;
    public readonly MaterialParameters Material;
}

public static class CompositeProgram
{
    public static ShaderAbi Abi { get; }

    public static int PackParameters(
        in CompositeParameters value,
        Span<byte> destination);

    public static int PackVertexElements(
        ReadOnlySpan<VertexInput> values,
        Span<byte> destination);
}
```

The actual generated type is specific to the selected layer set. It may retain
an original user value type as a field, or expose generated root helpers, but
its members are created only from live host-visible roots. The Engine references
the producer assembly containing this generated facade and therefore does not
construct the original layer contexts.

The compiler also emits a deterministic mapping:

```text
source context field/member
    -> full source symbol identity
    -> stable composite port identity
    -> generated host-projection member or packer root
    -> final ShaderAbi resource/push-constant/vertex-input entry
```

The source symbol identity is used only during compilation. The generated host
projection and cached Editor adapter use the stable generated port identity;
they never match by short CLR field name. If two source contexts expose the
same semantic value for the same logical port, composition unifies them. If
their roles, types, access, or resource declarations conflict, compilation
fails with a diagnostic rather than guessing.

For a dynamically selected Editor composite, a concrete CLR type cannot be
known by the Engine before the selection is made. In that lane the generated
producer package exposes a host-only schema and typed port tokens:

```csharp
var composite = editor.GetCompiledComposite(selection);
var values = composite.CreateValueSet();

values.Set(composite.FramePort, frame);
values.Set(composite.MaterialPort, material);

composite.PackParameters(values, destination);
```

`FramePort` and `MaterialPort` are generated tokens carrying the expected
value type and stable port identity. The Editor can enumerate the schema to
show required values, defaults, resources, and update scope. The adapter calls
generated typed pack methods; it does not discover offsets or reconstruct a
source context with reflection. A host-only erased value set is acceptable at
the inspector/selection boundary, but it is resolved to generated code before
the Render command and is not visible to shader lowering or Vulkan submission.

The host projection has four deliberately separate categories:

- **Host values:** live push-constant roots and ordinary vertex-input values;
  the generated projection/packer tells Engine which values are required.
- **Resources:** typed texture, sampler, and storage-buffer handles plus
  generated element packers; Render binds handles using the final ABI.
- **Interstage values:** values produced by the vertex/layer chain and consumed
  by later stages; Engine never supplies or packs them.
- **Compile-time values:** constants and explicit providers; they are lowered
  into the shader and do not become host ports.

An unused source-context field is absent from the projection. An optional field
with a documented default is represented by that default provider. A required
host root with no value is a composition/command-building error. There is no
silent zero-fill for a missing required value.

Thus the Engine has two valid ways to connect CLR data to a composite:

- reference the generated concrete `CompositeProgram` for a static engine
  shader and call its typed wrapper;
- hold the selected generated composite adapter and populate its generated
  ports for an Editor-driven dynamic selection.

In both cases the adapter produces packed bytes before handing the command to
Render. Render receives only the final artifact, `ShaderAbi`, resource handles,
and packed byte ranges. The final artifact does not need to expose CLR types,
Roslyn symbols, or the generated host schema.

An `IShaderPacker<T>`-style interface is optional implementation machinery for
the typed lane. It must not be required on user structs and must not be added
to `DeltaShader.Contract` merely to expose a common method. If an erased
interface is useful for Editor selection, it belongs to host/tooling code and
contains an `ShaderAbi` reference plus operations that delegate to generated
packers. It is not a new ABI or a generic runtime shader API.

## What is and is not forbidden

The purpose of this boundary is to prevent accidental invalid data, not to
pretend that unsafe code cannot exist.

The public User API must not offer an ordinary dispatch overload that accepts
arbitrary CLR structs or user-created raw bytes in place of the generated
packer. It exposes typed contexts and generated dispatch wrappers.

The producer/Render handoff may transport a `Span<byte>` or equivalent staging
memory after a generated pack operation. That byte transport is an internal
host detail, not a user authoring contract. Advanced native code can always
write raw memory deliberately; it is outside the safe DeltaShader API and is
not validated by adding another shader graph system.

The following are never allowed in the shader path:

- reflection-based layout discovery;
- `object` values, delegates, dynamic dispatch, or expression trees;
- runtime Roslyn, GLSL, or SPIR-V compilation;
- a second JSON manifest or copied ABI model;
- raw CLR struct copies as a substitute for generated packing.

An Editor-only value bag or erased adapter is not a shader path. It is allowed
only when it resolves to generated code before the Render command is submitted.

## Packing domains

All host-visible domains use the same rule: generated methods are specialized
to the final artifact and write resolved ABI positions explicitly.

### Push constants

Push constants are not passed directly as a CLR struct. Vulkan receives a byte
range, and the range can contain padding, alignment gaps, or only live members
after compiler liveness analysis.

The generator emits a root helper for each push-constant root and, where useful,
member/value helpers. The helper:

- validates destination capacity;
- clears the resolved range so padding is deterministic;
- writes each live member at its ABI offset;
- uses the resolved representation for bool and numeric values;
- uses matrix stride and column-major order for matrices;
- does not pack resources or descriptor handles into the push-constant range.

Frame-wide values such as resolution, time, camera data, and material values
can therefore be updated once per draw or per batch. Per-instance data must
not be moved into push constants merely to avoid writing an SSBO.

### Storage-buffer elements

For a storage buffer, generated helpers exist at element and range level:

```text
Pack<Resource>Element(value, destination)
Pack<Resource>Elements(values, destination)
Unpack<Resource>Element(source)
Unpack<Resource>Elements(source, destination)
```

The range helper uses the resolved array stride. A caller updating one indexed
instance may pack directly into the corresponding `index * ArrayStride` byte
offset in a persistent staging or mapped upload region. It does not create a
new draw or dispatch for that instance.

Nested value structs are recursively packed by generated code. `float3`,
rectangular matrices, quaternion values, arrays, and explicit padding follow
the ABI metadata rather than the CLR representation. Unsupported references,
auto-layout structs, and ambiguous bool representations remain compiler
diagnostics.

When a shader has multiple storage-buffer bindings, the generated program also
exposes a range plan such as `Get<Method>StorageBufferRanges`. It returns the
resolved set/binding, byte offset, byte size, and element stride for every
resource into a caller-provided span. `Get<Method>StorageBufferByteLength`
returns the size of one backing allocation. The host may pack each resource with
its generated element helper into the corresponding range and bind descriptor
ranges that alias the same GPU buffer. This reduces physical buffer allocation
and staging ownership without changing descriptor bindings or the final ABI.
`Std430Packer.GetRange` supplies the checked byte span used by those generated
helpers, so consumers do not duplicate offset/size slicing logic.

### Vertex buffers

Vertex packing is the same generated operation applied to a vertex binding.
It is not a renderer-owned reinterpretation of a CLR struct.

The generator emits one element/range helper per resolved vertex binding when
the artifact uses multiple bindings:

```text
PackVertexBinding0Element(value, destination)
PackVertexBinding0Elements(values, destination)
PackVertexBinding1Element(value, destination)
PackVertexBinding1Elements(values, destination)
```

Each helper is generated from `ShaderAbi.VertexInputs` and the matching
`ShaderAbi.VertexBuffers[binding]` entry. It uses each input's location, binding,
byte offset, value format, and the binding stride. The host can keep one
persistent vertex buffer and update indexed ranges without changing the draw
count or creating one binding per object.

`Get<Method>VertexBufferRanges` supplies the binding-specific offsets and sizes
for a shared backing allocation. The descriptor/binding count remains the
resolved ABI count; the optimization removes per-stream buffer allocations and
does not invent a new vertex layout.

Vertex inputs and interstage values are still different ABI sections. Their
shader-visible semantic types can be the same, but only vertex inputs receive
host upload metadata. Interstage values are produced by the vertex stage and
are never packed by the host as if they were vertex data.

### Resources and descriptors

The resource field in a user context is a typed handle/value declaration, not
the bytes of the resource contents. Generated code provides the resource slot
and element packer where applicable:

- a storage-buffer handle is bound by the Render adapter;
- storage-buffer elements are packed by generated element/range helpers;
- sampled textures and samplers are passed as resource handles and are not
  serialized into std430 bytes;
- descriptor set/binding and access come from the resolved artifact ABI;
- the host never derives access from the CLR class name.

This keeps texture descriptors, SSBO contents, and push constants separate
without requiring users to write separate layout declarations for every
shader-specific byte range.

## User scripts, engine shaders, and composites

The same boundary covers the full range of authoring workflows.

### Individual user script

A user writes a context and calls the generated dispatch/draw wrapper. The
context may contain constants, resource handles, and user value structs. The
wrapper selects the generated packers and submits one command. The user never
needs to know whether a value became a push constant, an SSBO element, or a
vertex attribute; that decision is visible in the selected `ShaderAbi`.

### Engine/base shader

An engine shader uses the same static entry-point contract. Its generated
program is a reusable producer package containing SPIR-V, resolved ABI access,
typed packers, and any host-facing factory methods. Engine code can use a
strongly typed context and can update persistent buffers by generated stride
without owning compiler or layout code.

### Composite shader

The Editor selects layers and asks DeltaShader tooling to produce one final
composite artifact. The final generated packer is for the final composite, not
one packer per GPU layer.

Composition has two separate data flows:

```text
host-visible context values
    -> final composite packer
    -> push constants / resources / vertex inputs

vertex and layer semantic values
    -> compiler-resolved interstage chain
    -> fragment stage
```

Interstage payloads are not copied through host buffers. The compiler merges
semantic fields by their full semantic symbol identity and produces one
physical interface for the selected chain. A layer may read, write, or forward
a field; dead fields are removed before the final ABI is emitted. The final
packer sees only live host-visible roots in the final artifact.

If two layers use the same CLR value type in different logical roles, their
full source symbols or explicit semantic ports keep those roles distinct. A
short field name is never enough to join composition data.

The Editor may dynamically change the layer stack, but every selected stack
must resolve to a cached generated program/packer pair before drawing. There
are no runtime C# method translations, runtime GLSL compilations, or multiple
GPU entry points hidden behind one logical draw.

### Shader graph and other frontends

A graph frontend, material editor, or future authoring frontend may emit the
same static shader entry point and semantic/resource declarations. It reuses
the existing compiler diagnostics and generated packer. It does not need a
parallel validator for every graph node or a graph-specific ABI.

The minimal graph checks are therefore ordinary compiler checks:

- unsupported shader-visible type or operation;
- missing producer or final output;
- resource/layout conflict;
- missing host value for a live generated root;
- generated packer capacity or artifact identity mismatch.

Everything else is handled by the frontend's own authoring UX before it emits
the static source. This keeps the graph system thin and keeps shader semantics
owned by DeltaShader.

## ABI identity and validation

Packing correctness needs one cheap identity check, not a second validation
framework.

For a generated static program, the relationship is compile-time fixed. The
generated factory exposes the final ABI accessor and packer methods from the
same generated source. A typed call cannot accidentally select the packer for
another shader program.

For an Editor-selected program, the cached adapter stores:

- the final artifact/program identity;
- the generated packer identity;
- the same `ShaderAbi` reference used to create the Render command;
- the resolved semantic/resource port mapping used during composition.

The adapter validates this relationship once when the cache entry is created.
It does not inspect every byte on every dispatch. A mismatch invalidates the
cache entry and requests regeneration; it never falls back to guessed offsets.

Generated packers perform only local safety checks at call time:

- destination length and array capacity;
- valid element/range bounds;
- required value presence for a generated root;
- documented conversion rules for host values.

Compiler diagnostics remain authoritative for shader source. ABI validation
remains authoritative for the final artifact. Render validates device limits
and owns Vulkan resources. No layer duplicates another layer's validation.

## Ownership and lifetime

| Concern | Owner |
|---|---|
| Shader-visible types and operation semantics | Delta.Maths |
| Layout calculation and generated pack/unpack source | DeltaShader |
| Static generated program and typed host wrapper | DeltaShader producer/tooling |
| Editor layer selection and composite cache key | DeltaEditor/Editor host |
| Temporary packed bytes and staging lifetime | Calling host / DeltaRender adapter |
| Vulkan buffers, descriptors, uploads, readback, device limits | DeltaRender |
| Ordinary engine value contexts and frame policy | Engine |
| XAML paint/layout values | DeltaXAML |

DeltaRender receives the final `ShaderArtifact`, its `ShaderAbi`, and packed
bytes produced by generated DeltaShader code. It must not recreate a matrix
stride, infer a vertex offset, or use a local `ShaderAbiValueCodec` for a
producer that already has generated packers.

DeltaShader does not own GPU allocation, descriptor lifetime, or Vulkan feature
checks. DeltaXAML and Engine do not know `std430` or Vulkan offsets.

## Performance model

The design avoids an object-per-dispatch and an object-per-instance policy.

- Static generated packers are static methods or cached value adapters.
- Array helpers write directly to caller-provided staging memory.
- Partial updates use resolved element/vertex stride and indexed offsets.
- Push constants are packed once per change in the relevant update scope.
- Editor reflection or value-bag work happens while building/cacheing a command,
  not inside shader execution or the Vulkan inner loop.
- One composite produces one final artifact and one draw/dispatch path; layers
  do not create one render call each.

The packer does not promise that arbitrary values are free to allocate. It
provides allocation-free generated methods for the hot path and leaves
temporary memory ownership to the caller.

## Failure behavior

Failure must be visible at the earliest owner boundary:

- unsupported source construct: compiler diagnostic;
- unsupported or ambiguous shader-visible field: compiler diagnostic;
- unresolved composite producer: composition diagnostic;
- missing external tool while publishing: tool error;
- stale generated packer/artifact pair: cache invalidation and regeneration;
- insufficient destination: argument/capacity error;
- unsupported device capability or push-constant limit: Render diagnostic;
- corrupted SPIR-V or invalid final ABI: artifact validation failure.

No failure is repaired by silently using CLR layout, a default offset, a raw
copy, a guessed binding, or a mass suppression.

## Implementation order

The design can be adopted without a contract revision:

1. Keep the existing generated context, root, SSBO, vertex, and unpack helpers
   as the typed lane.
2. Ensure every generated program exposes its cached final ABI accessors and
   direct root packers for push constants.
3. Generate one packer surface per resolved vertex binding and per writable
   output, including matrix stride and multi-output `out` values.
4. Publish the generated packer source/assembly with producer packages so
   conformance and Render consumers do not create local codecs.
5. Add a small host-only adapter/registry for Editor-selected composite
   artifacts; keep it outside `DeltaShader.Contract`.
6. Migrate conformance and Render consumers from handwritten ABI codecs to the
   generated methods.
7. Keep compiler, artifact, and Render checks separate: compiler/golden checks
   prove lowering and ABI; generated packer tests prove offsets; Render tests
   prove device submission and CPU/GPU results.

This order preserves the current frozen runtime boundary, supports static user
scripts and engine shaders immediately, and leaves dynamic composition as
selection of already-generated code rather than a new runtime compiler.

## Non-goals

This design does not:

- add a new `ShaderAbi` packing-plan type;
- require user structs to implement a packer interface;
- expose Roslyn symbols, GLSL, IR, JSON manifests, or generic live values to
  Render;
- make `std430` part of the user authoring syntax;
- compile or combine arbitrary C# at runtime;
- turn every composite layer into a separate render pass;
- create a second validation architecture for shader graphs.
## Shared physical backing buffers

Generated `Get*StorageBufferRanges` and `Get*VertexBufferRanges` methods describe
logical descriptor or vertex-input ranges in one physical byte buffer. A
consumer may allocate that buffer once and obtain each view with
`Std430Packer.GetRange(backing, range)`.

The generated range plan is the source of truth for offsets, sizes, alignment,
and element stride. Consumers must not recompute those values from CLR layout,
`Marshal.SizeOf`, or `MemoryMarshal`; descriptors and vertex bindings may still
refer to different ranges of the same allocation.
