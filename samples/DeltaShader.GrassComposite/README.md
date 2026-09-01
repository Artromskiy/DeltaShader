# DeltaShader grass composite sample

This sample contains independent layer candidates for an editor-selected grass
composite:

- `TransformAndInstance` supplies transformed position, world position, normal,
  UV and vertex color from vertex inputs and an instance storage buffer.
- `TexturedLambert` and `SolidLambert` provide textured and local-color paths.
- `LocalPhong`, `Toon`, `Pbr` and `FakeTranslucent` provide alternative shading
  layers.

The editor selects an ordered set of these entry points and asks the compiler to
resolve their contexts by semantic type identity. The compiler then lowers the
selected layer chain into one final graphics artifact. This sample intentionally
does not perform runtime C# composition or contain generated artifacts.

`GrassPayload` is the shared logical interstage contract. Its `[Layout]` fields
are host-provided vertex inputs; the remaining semantic fields are produced by
the vertex layer and are available to downstream fragment layers. The resolved
compiler plan is available through `ShaderCompiler.ResolveCompositeContext`.
