# DeltaShader

DeltaShader lets you author Vulkan shaders in C# and produce validated GLSL,
SPIR-V and `ShaderAbi` data for a renderer.

## What it provides

- C# entry points for compute, vertex and fragment shaders.
- Typed value contexts for resources, push constants and stage data.
- `Delta` vector, matrix, quaternion and intrinsic operations.
- Compile-time diagnostics for unsupported shader code and invalid layouts.
- Generated shader artifacts and typed pack/unpack helpers for consumers.

## Quick start

Add the packages to the project that owns the shader source:

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

```csharp
using Delta;
using Delta.Shader;

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

The project produces validated stage source and binary artifacts, a resolved
ABI and generated typed accessors for the owning application.

## Core concepts

Shader source -> diagnostics -> GLSL/SPIR-V + `ShaderAbi` -> renderer

Contexts are blittable value types. Resources, push constants and stage data
are declared through the public API; consumers use generated helpers instead
of reproducing GPU layout rules.

## Capabilities and limits

DeltaShader targets the Vulkan GLSL 460/SPIR-V profile. Shader code must be
static, shader-visible C# using supported value types and operations. Managed
references, classes, delegates, reflection, runtime services and runtime shader
compilation are not shader inputs. Graphics programs require a valid
vertex/fragment interface.

## Packages and examples

- [DeltaShader.Tool on NuGet](https://www.nuget.org/packages/DeltaShader.Tool/)
  provides project integration and artifact generation.
- [DeltaMaths on NuGet](https://www.nuget.org/packages/DeltaMaths/) provides
  shader-visible math types and operations.
- [Shader examples](https://github.com/Artromskiy/DeltaShader/tree/main/samples)
  show compute and graphics authoring.

## Further reading

- [User API](docs/USER_API.md)
- [Final artifact contract](docs/CONTRACT.md)
- [Documentation index](docs/README.md)
