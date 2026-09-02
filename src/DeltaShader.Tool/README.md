# DeltaShader.Tool

`DeltaShader.Tool` integrates DeltaShader into a project that owns C# shader
source. It emits validated GLSL, SPIR-V and `ShaderAbi` sidecars for the
project's renderer.

## Installation

Add the tool, compiler and analyzer packages to the shader project:

```xml
<PropertyGroup>
  <DeltaShaderEnabled>true</DeltaShaderEnabled>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="DeltaShader.Compiler" Version="*" PrivateAssets="all" />
  <PackageReference Include="DeltaShader.Analyzers" Version="*" PrivateAssets="all" OutputItemType="Analyzer" />
  <PackageReference Include="DeltaShader.Tool" Version="*" PrivateAssets="all" />
  <DeltaShaderSource Include="Shaders/**/*.cs" />
</ItemGroup>
```

`DeltaShaderSource` can select shader files explicitly. When it is omitted,
the integration discovers `Shaders/**/*.cs` and then uses the project's normal
C# compile items.

## Result

The project receives validated `.spv`, `.glsl`, `.shader.json` and `.abi.json`
outputs below its normal build output. Generated C# accessors expose the final
`ShaderArtifact`, `ShaderAbi` and typed pack/unpack helpers. Consumers should
use those generated APIs instead of reading sidecars or calculating layout.

The package has no Vulkan runtime dependency. A renderer consumes the final
artifact boundary; it does not compile shader C# or infer GPU layout.

See the [DeltaShader user API](../../docs/USER_API.md) for authoring syntax and
the [final artifact contract](../../docs/CONTRACT.md) for the public handoff.
