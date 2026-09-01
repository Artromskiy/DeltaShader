# MSBuild integration

This document describes the optional project build integration. It is a
tooling feature, not a runtime contract and does not add a Vulkan or Render
dependency.

## Enable it in a shader project

Install the `DeltaShader.Tool` package and opt in from the project file:

```xml
<PropertyGroup>
  <DeltaShaderEnabled>true</DeltaShaderEnabled>
  <DeltaShaderOptimization>performance</DeltaShaderOptimization>
</PropertyGroup>

<ItemGroup>
  <DeltaShaderSource Include="Shaders/**/*.cs" />
</ItemGroup>
```

`DeltaShaderSource` is optional for discovery. When it is omitted, the build
target first discovers `Shaders/**/*.cs`; projects without that directory
fall back to their normal `Compile` items as incremental inputs. The tool
still receives the complete project so Roslyn resolves all source and project
references normally. Entry points are selected by the existing
`ComputeShader`, `VertexShader` and `FragmentShader` attributes.

The solution does not need a shader-specific entry. A normal project in the
solution is enough; its MSBuild target runs after the project has compiled.

## Build result

One invocation of `DeltaShader.Tool` compiles the complete project and emits
validated GLSL, SPIR-V and shader manifests. The default paths are:

```text
obj/<Configuration>/<TargetFramework>/DeltaShader/<configuration-key>/
bin/<Configuration>/<TargetFramework>/DeltaShader/
```

The first path is staging/intermediate output. The second path is the exact
runtime deployment directory. Only shader output extensions are copied:
`*.spv`, `*.glsl`, `*.shader.json` and `*.abi.json`. Lock files and temporary
files never become publication members.

The target is incremental. It includes project sources, the project file,
project assets and the tool path in its inputs. Profile, SPIR-V, GLSL and
optimization settings are part of the output key. A failed compile does not
replace the previous published directory.

Downstream MSBuild targets can consume the generated item:

```xml
<Target Name="DescribeDeltaShaderArtifacts" AfterTargets="DeltaShaderCompile">
  <Message Importance="High" Text="Shader manifest: %(DeltaShaderArtifact.ManifestPath)" />
  <Message Importance="High" Text="Shader SPIR-V: %(DeltaShaderArtifact.SpirvPath)" />
</Target>
```

The item is derived from the generated sidecars; it is not a second ABI or a
replacement for `DeltaShader.Contract.ShaderArtifact`.

## Local checkout

When developing DeltaShader itself, the package path is not available. Set
the tool path explicitly and import the same build files from the checkout:

```xml
<PropertyGroup>
  <DeltaShaderEnabled>true</DeltaShaderEnabled>
  <DeltaShaderToolPath>$(MSBuildThisFileDirectory)..\..\src\DeltaShader.Tool\bin\Release\net10.0\DeltaShader.Tool.dll</DeltaShaderToolPath>
</PropertyGroup>

<Import Project="$(MSBuildThisFileDirectory)..\..\src\DeltaShader.Tool\build\DeltaShader.props" />
<Import Project="$(MSBuildThisFileDirectory)..\..\src\DeltaShader.Tool\build\DeltaShader.targets" />
```

The tool must be built before the shader project. In a normal application,
use the package so restore/build ordering is handled by NuGet and MSBuild.

## Ownership

The project owns C# source. `DeltaShader.Tool` owns compilation and final
artifact publication. Render and Engine consume the published artifact or a
generated `ShaderArtifact`; they do not calculate ABI offsets, parse GLSL or
run the compiler themselves.
