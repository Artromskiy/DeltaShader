# DeltaShader package boundaries

DeltaShader publishes four packages with separate responsibilities:

| Package | Consumer | Contents |
| --- | --- | --- |
| `DeltaShader.Contract` | Runtime consumers | `ShaderArtifact`, `ShaderAbi`, and related neutral data types in `lib/`. |
| `DeltaShader.Compiler` | Build-time compiler integrations | Roslyn frontend, typed shader IR, and compiler APIs in `lib/`. |
| `DeltaShader.Analyzers` | Roslyn compiler | Analyzer and generator assemblies under `analyzers/dotnet/cs/`. |
| `DeltaShader.Tool` | Shader producer projects | CLI files under `tools/net10.0/any/` and MSBuild imports under `buildTransitive/`. |

`DeltaShader.Backend.Glsl` is an internal implementation project and is not a
runtime-facing package. It is included by the compiler/tool build where needed.

`DeltaShader.Tool` is referenced with `PrivateAssets="all"` by shader producer
projects. It is not a dependency of runtime consumers. Its generated outputs
remain project build outputs and are not copied into consumer repositories.

The contract package contains no Roslyn, MSBuild, compiler, backend, Vulkan, or
renderer dependency. Render and Engine consume only the final artifact and
resolved `ShaderAbi`.

All packages are restored from `nuget.org`. Package versions must follow the
repository release versioning rule in `WORKFLOW.md`; a contract or generated
pack/unpack API change increments both the tag and package version.

## UI, text, and mesh producer projects

`DeltaShader.UI`, `DeltaShader.Text`, and `DeltaShader.Mesh` remain source-only
producer projects. They contain authoring sources and generate artifacts during
the owning build; they are not runtime package replacements for
`DeltaShader.Contract`. Render consumers must receive the generated artifact
and resolved `ShaderAbi` from the producer build rather than a package that
would recreate or duplicate that ABI. These projects should become packages
only when their generated artifact payload and consumer API have a stable
publication contract.
