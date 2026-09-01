# Shader composition design

This document describes the compile-time shader-composition model. The current
compiler accepts the standard semantic value types in individual graphics
entry points; editor-selected multi-layer chain lowering remains a separate
implementation milestone. The supported entry-point surface is documented in
[USER_API.md](USER_API.md).

## Goal

A composite is an editor-selected ordered set of independent vertex and
fragment/material shader layers. DeltaShader compiles that set into one final
vertex module, one final fragment module, one `ShaderArtifact` and one resolved
`ShaderAbi`. Runtime does not combine C# shaders or execute several Vulkan
entry points for one draw.

```text
editor layer selection
    -> typed semantic dataflow
    -> one logical composite state
    -> one vertex/fragment interface
    -> one ShaderArtifact + ShaderAbi
```

## Semantic value types

Interstage matching uses the full symbol identity of a semantic value type, not
the CLR field name and not the underlying scalar/vector shape. Standard types
are supplied for common meanings by `Delta.Shader`:

```text
Position
Uv0
Color
WorldPosition
WorldNormal
```

Two user types containing the same `float2` are not implicitly compatible. A
user-defined semantic type or an explicit compiler adapter is required for an
intentional conversion.

The compiler uses Roslyn symbol identity while composing. The final artifact
stores a stable semantic/port identity together with the resolved type and
physical location. Raw Roslyn symbols never cross the runtime boundary.

## Payloads are typed patches

An interstage payload is a set of values produced or changed by one layer. It
does not have to repeat the complete composite state.

```csharp
public struct InterstageVertex
{
    public Position Position;
    public Uv0 Uv;
    public VertexColor VertexColor;
}

public struct InterstageUvAnimation
{
    public Uv0 Uv;
}

public struct InterstageFragment
{
    public Color Color;
}
```

The semantic types provide field meaning, so field names are not used to match
ports. A payload that omits `Color` forwards the previous value; it does not
clear or overwrite it.

`Position` is special: exactly one vertex output must provide it and it lowers
to `gl_Position`. It is not an ordinary varying. `Color` is the required final
fragment semantic: at least one fragment-side payload must provide or carry it
to the final fragment result. A separate `FragmentOutput` wrapper is not
required.

## Stage contexts

Contexts are input declarations for one composite, not interstage storage.
They may contain different fields in different stages:

```csharp
public readonly struct VertexContext
{
    public InterstageVertex Input;
    public ObjectColors ObjectColors;
    public FrameConstants Frame;
}

public readonly struct FragmentContext
{
    public Uv0 Uv;
    public Color Color;
    public FrameConstants Frame;
    public MainTexture Texture;
}
```

The context merger uses full type/member identity. It collects fields used by
selected layers, removes dead fields, merges equal declarations and reports
conflicting types or resource meanings. Contexts are not required to be
structurally identical.

In the target design, a user-defined resource type carries its resource kind,
element type and access contract. The composite compiler assigns descriptor
coordinates after composition. A value context type supplies push-constant
data after liveness analysis. Explicit layout markers are therefore not needed
in the canonical composite surface; resolved coordinates and byte layout remain
visible in `ShaderAbi`.

## Composition and chain behavior

Semantic payloads are now the canonical source form for graphics stages. This
does not make the current compiler dynamically combine arbitrary methods: the
editor/tooling layer must select and compile the composite before runtime.

The editor selects layers by their full source symbols and preserves their
explicit order. A layer can read, write or leave a semantic untouched:

```text
Geometry layer:
  produces Position, Uv0, VertexColor

UV animation layer:
  reads/writes Uv0
  forwards Position and VertexColor

Texture layer:
  reads Uv0 and Color
  writes Color

Final fragment layer:
  reads Color
  provides the final fragment Color
```

The default merge policy is `chain`. A later layer does not need to write a
field merely because it appears in an earlier payload. The logical composite
state keeps the last produced value and forwards it until a later layer reads
or changes it. If no downstream stage reads a field, liveness analysis removes
it from the physical interface.

The compiler starts from final fragment requirements and resolves producers
back through the layer chain. A required semantic is reported only when no
earlier producer, host-provided input, builtin or explicit constant/provider
can supply it. Duplicate writers are valid when their order and chain behavior
are explicit; ambiguous or cyclic dependencies are diagnostics.

## Final ShaderAbi

The ABI has visibly separate external-input and interstage sections:

```text
VertexInputs
  semantic, type, location, binding, offset, stride, format, required-by-host

Interstage
  semantic, type, location, producer, consumer, interpolation, required-by-chain

Resources
  semantic/type identity, set, binding, kind, access, stage visibility, layout

PushConstants
  type/member identity, offset, alignment, size, stage visibility
```

Vertex inputs and interstage values use the same shader-visible semantic types,
but vertex inputs additionally describe how the host supplies the bytes.
Interstage locations are assigned after composition and may differ between
composites. The host reads the selected artifact ABI; it does not assume global
locations, bindings or strides.

Only live fields are emitted. This reduces interpolation and bandwidth cost
without changing logical chain semantics. Unsupported vertex formats, missing
required host inputs and resource conflicts prevent artifact publication rather
than being silently ignored.

## Editor and runtime ownership

The editor owns layer selection, composition requests and the cache key. A
composition key includes selected source identities, their order, relevant
contract fingerprints and backend/profile. DeltaShader owns validation, semantic
resolution, lowering, ABI generation and artifact publication.

DeltaRender receives only the final `ShaderArtifact` and `ShaderAbi`; it owns
pipeline creation, descriptor allocation and GPU lifetime. Runtime C#
transpilation and runtime layer merging are not part of this design.

## Required implementation slices

The compiler work needed to make this design real is bounded to:

1. represent semantic typed patches and full-symbol layer selection;
2. resolve context/resource declarations without field-name matching;
3. perform producer lookup, chain forwarding and dead-field elimination;
4. emit one final vertex output and fragment input interface;
5. generate the final ABI and typed packers from that ABI;
6. add diagnostics for missing producers, duplicate/ambiguous writers, cycles,
   type conflicts and unsupported stage declarations.

No second runtime ABI, Vulkan-specific authoring type or runtime compiler is
needed.
## Current implementation status

The first compiler slice is now present. `ShaderIrModule.ContextFields` keeps
logical context declarations alongside, but separately from, the physical
interstage locations. `ShaderCompiler.ResolveCompositeContext` consumes the
successful layer results and produces a deterministic compiler-side plan for
interstage semantic values, resources and push constants. The selected stack
can then be passed to `ShaderCompiler.ComposeGraphics`, which returns one
compiler IR module for each graphics stage with canonical names and concatenated
layer bodies.

The resolver keys interstage fields by the full semantic value type identity,
and resources/push constants by their full declared type identity. Source field
names are display-only and are never used as composition keys. Different layer
names can therefore contribute the same `Position`, `Uv0` or `VertexColor`
semantic without requiring identical C# member names. Incompatible GLSL type,
resource access or push-constant layout produces a compiler diagnostic.

The result is still compiler IR, not a final composite artifact. Producer/
consumer liveness for omitted fields, final artifact publication and the
generated composite packer remain the next bounded compiler slices. The editor
can already use the resolver to reject missing producers and incompatible
layouts before requesting those outputs.

The reference layer set is
`samples/DeltaShader.GrassComposite/GrassComposite.cs`: it covers transform and
instance data, textured/solid color, Lambert, Phong, toon, PBR and fake
translucent candidates without introducing runtime compilation.

## Compiler composition API

```csharp
ShaderCompositeCompilationResult composite = ShaderCompiler.ComposeGraphics(
    vertexLayers,
    fragmentLayers);
```

The layer lists contain successful `ShaderCompilationResult` values in editor
order. `composite.Context` contains the merged declaration plan;
`composite.Vertex` and `composite.Fragment` contain the compiler IR modules
that can be passed to `Delta.Shader.Backend.Glsl.GlslEmitter`. The compiler
rewrites layer-local semantic names to canonical interstage names, preserves
host vertex-input reads, and keeps resources/push constants in the compiler
side plan. These are tooling representations only; no runtime C# compilation,
delegates or reflection are introduced.

`ShaderCompositeCompilationResult.GetBuildManifest(stage)` materializes the
existing build-time manifest for a selected stage. Resource declarations are
stage-specific: a vertex-only instance buffer is not emitted into the fragment
module, and a fragment-only texture is not emitted into the vertex module. A
producer tool can pass these manifests and externally compiled SPIR-V to
`Delta.Shader.Tool.ShaderCompositeArtifactPublisher.Create`; it returns the
existing `GraphicsShaderProgram` and therefore the existing final
`ShaderArtifact`/`ShaderAbi` boundary. It does not introduce a second ABI or
runtime compilation path.

For an editor-selected stack, `ShaderCompositeSourceGenerator.TryGenerate`
accepts the selected vertex and fragment Roslyn method symbols plus that
composition result. It emits one generated graphics program with
`VertexAbi`/`FragmentAbi`, `CreateProgram`, and uniquely prefixed typed packers
for the selected source contexts. The packers continue to accept the original
CLR context/root types, so the editor does not need to invent a CLR type for
the merged context. The generated program still consumes externally produced
SPIR-V and remains a tooling/build-time output.
