# DeltaShader.Tool

`DeltaShader.Tool` compiles C# shader projects into validated GLSL, SPIR-V and
`ShaderAbi` sidecars. It is a build package: add it as a private
`PackageReference` to the project that owns the shader source.

## Enable MSBuild integration

```xml
<PropertyGroup>
  <DeltaShaderEnabled>true</DeltaShaderEnabled>
</PropertyGroup>

<ItemGroup>
  <!-- Compiler supplies the Delta.Shader authoring symbols. -->
  <PackageReference Include="DeltaShader.Compiler" Version="*" PrivateAssets="all" />
  <!-- Analyzer supplies compile-time shader validation. -->
  <PackageReference Include="DeltaShader.Analyzers" Version="*" PrivateAssets="all" OutputItemType="Analyzer" />
  <PackageReference Include="DeltaShader.Tool" Version="*" PrivateAssets="all" />
  <DeltaShaderSource Include="Shaders/**/*.cs" />
</ItemGroup>
```

The three package roles are deliberately separate: `DeltaShader.Compiler`
provides the compile-time `Delta.Shader` authoring assembly,
`DeltaShader.Analyzers` provides Roslyn diagnostics, and `DeltaShader.Tool`
provides the MSBuild target/CLI that emits final artifacts. A shader project
must reference all three; the tool package intentionally has no compile-time
or analyzer asset.

`DeltaShaderSource` is optional. If it is omitted, the build integration first
discovers `Shaders/**/*.cs` and then falls back to the project's normal
`Compile` items. The tool receives the complete project so Roslyn resolves
references and shader symbols normally.

The build target publishes validated shader output below
`bin/<Configuration>/<TargetFramework>/DeltaShader/<AssemblyName>/`. It publishes only
`.spv`, `.glsl`, `.shader.json` and `.abi.json`; lock files and temporary files
are not runtime inputs. The generated C# program/factory surface is the normal
consumer path: use its final `ShaderArtifact`, `ShaderAbi`, cached stage ABI
accessors and typed pack/unpack helpers. Do not parse sidecars or duplicate
layout calculations in a consumer. The direct CLI output path is reserved for
bounded validation and Maths conformance bundles.
Generated shader files are not package or runtime artifact members of the tool
package.

The package contains no Vulkan runtime dependency. Render and Engine consume
the final `ShaderArtifact`/`ShaderAbi` boundary and do not compile C# or
calculate ABI layout.

## Publish the package

After building the package, publish the exact version to NuGet with an API key
provided through the environment:

```bash
dotnet nuget push artifacts/DeltaShader.Tool.0.0.18.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" \
  --skip-duplicate \
  --no-symbols
```

The package version must match the repository release tag. Do not put the API
key in project files, shell history or committed documentation.
