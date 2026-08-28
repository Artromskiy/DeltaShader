# DeltaShader.Playground

Small user-facing project for writing C# compute shaders and immediately
checking their Vulkan artifacts.

`Compute.cs` and `AddBias.cs` contain two editable compute entry points:

- `SequenceMovement`: `output[id] = output[id] + input[id]`;
- `AddBias`: `output[id] = input[id] + 7u`.

`Jfa.cs` contains the graphics form of the original JFA outline flow:

- `jfa-init` samples the silhouette and writes an encoded nearest-seed
  texture;
- `jfa-flood` samples the previous seed texture at eight jump offsets and is
  repeated with ping-pong render targets;
- `jfa-composite` samples the final seed texture and silhouette, then writes
  the anti-aliased outline color.

Each pair uses a fullscreen triangle (`vkCmdDraw(3, 1, 0, 0)`) and returns a
fragment color to the current render target. Render owns render targets,
descriptor updates, sampler state, and flood-pass sequencing. The shader
source does not own Vulkan objects or execute a compute dispatch. The source
being recreated is `JFAOutlineShader.shader` in the CozyKitchen project.

They are two small shader modules because the current compiler contract allows
one compute entry point per module. `ComputeContext.cs` is shared by both
modules; the playground remains one user-facing folder and one VS Code build
task.

The playground is intentionally nested in the DeltaShader repository but has a
separate project boundary, so the analyzer does not inspect host code. The
repository layout is documented in [repository-layout.md](repository-layout.md).

The authoring project is intentionally separate from the tooling host, so the
DeltaShader analyzer does not inspect renderer or host code. A normal Release
build runs the tool bridge automatically and prints one `PASS` line per shader
after `glslangValidator` and `spirv-val` succeed.

Both shader modules reference the sibling `DeltaMaths` project and can use its
canonical `Delta.Maths` types directly.

Open `DeltaShader.slnx` in VS Code, then use `Ctrl+Shift+B`, or run
`dotnet build src/DeltaShader.Playground/DeltaShader.Playground.csproj -c Release`. The solution includes
builds the `AddBias` module. The project references the sibling `DeltaMaths` project,
so the C# language server resolves `Delta.Maths` in the editor. Generated files are written to the ignored `artifacts/`
directory as `SequenceMovement.comp.*` and `AddBias.comp.*`.

The production contract is a compile-time static shader method; managed state,
implicit captures and closures are rejected. See
[../WORKFLOW.md](../WORKFLOW.md) for
the bounded command and [../TODO.md](../TODO.md)
for selected example work.
