# DeltaShader

DeltaShader lets you author Vulkan shaders in C# and produce validated GLSL,
SPIR-V and `ShaderAbi` data for a renderer.

## What it provides

- C# entry points for compute, vertex and fragment shaders.
- Typed value contexts for resources, push constants and stage data.
- `Delta` vector, matrix, quaternion and intrinsic operations.
- Compile-time diagnostics for unsupported shader code and invalid layouts.
- Generated shader artifacts and typed pack/unpack helpers for consumers.
- A stable handoff from shader authoring to a renderer without Vulkan types in
  shader source.

## Quick start

Add the authoring and build packages to the project that owns the shader source:

```xml
<PropertyGroup>
  <DeltaShaderEnabled>true</DeltaShaderEnabled>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="DeltaShader.Compiler" Version="*" PrivateAssets="all" />
  <PackageReference Include="DeltaShader.Analyzers" Version="*" PrivateAssets="all" OutputItemType="Analyzer" />
  <PackageReference Include="DeltaShader.Tool" Version="*" PrivateAssets="all" />
  <PackageReference Include="DeltaMaths" Version="*" />
  <DeltaShaderSource Include="Shaders/**/*.cs" />
</ItemGroup>
```

Write a static entry point using the public shader API:

```csharp
using Delta;
using Delta.Shader;
using static Delta.maths;

public readonly struct ComputeContext
{
    public readonly ReadOnlyStorageBuffer<float> Input;
    public readonly StorageBuffer<float> Output;
}

public static class ExampleShader
{
    [ComputeShader(localSizeX: 64)]
    public static void Double(in ComputeContext context)
    {
        uint index = ShaderBuiltins.GlobalInvocationId.X;
        context.Output[index] = context.Input[index] * 2.0f;
    }
}
```

The project produces validated stage source and binary artifacts together with
the resolved ABI and generated typed accessors for the owning application.

## Core concepts

Shader source -> compiler diagnostics -> GLSL/SPIR-V + `ShaderAbi` -> renderer

Entry-point contexts are ordinary blittable value types. Resource fields,
push-constant fields and stage payloads are described by the public API; the
consumer supplies CLR values and uses generated helpers rather than recreating
GPU layout rules.

## Capabilities and limits

DeltaShader targets the Vulkan GLSL 460/SPIR-V profile. Supported code must be
static, shader-visible C# and use supported value types and operations.

Managed references, classes, delegates, reflection, runtime services and
runtime shader compilation are not shader inputs. CLR layout is not used as a
GPU layout: unsupported fields and ambiguous stage interfaces produce compiler
diagnostics. Graphics programs require a valid vertex/fragment interface.

## Packages and examples

- [DeltaShader.Tool on NuGet](https://www.nuget.org/packages/DeltaShader.Tool/)
  adds project integration and artifact publication.
- [DeltaMaths on NuGet](https://www.nuget.org/packages/DeltaMaths/) provides
  shader-visible math types and operations.
- [Shader playground examples](https://github.com/Artromskiy/DeltaShader/tree/main/samples)
  show compute and graphics authoring.

## Further reading

- [User API](USER_API.md)
- [Final artifact contract](CONTRACT.md)
