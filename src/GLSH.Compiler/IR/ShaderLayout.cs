using System;
using System.Collections.Generic;

namespace DVG.Shaders.Compiler.IR;

public sealed class ShaderStd430Layout
{
    public const string Standard = "std430";

    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }

    public static ShaderStd430Layout ForGlslType(string? glslType)
    {
        glslType = string.IsNullOrWhiteSpace(glslType) ? "uint" : glslType;
        return glslType switch
        {
            "bool" or "int" or "uint" or "float" => Scalar(4),
            "bvec2" or "ivec2" or "uvec2" or "vec2" => Vector(8, 8),
            "bvec3" or "ivec3" or "uvec3" or "vec3" => Vector(16, 12),
            "bvec4" or "ivec4" or "uvec4" or "vec4" => Vector(16, 16),
            "mat2" => Matrix(8, 16, 8),
            "mat3" => Matrix(16, 48, 16),
            "mat4" => Matrix(16, 64, 16),
            _ => throw new ArgumentException($"Unsupported GLSL type '{glslType}'.", nameof(glslType))
        };
    }

    private static ShaderStd430Layout Scalar(uint size)
        => new() { Alignment = size, Size = size, ArrayStride = size };

    private static ShaderStd430Layout Vector(uint alignment, uint size)
        => new() { Alignment = alignment, Size = size, ArrayStride = alignment };

    private static ShaderStd430Layout Matrix(uint alignment, uint size, uint matrixStride)
        => new() { Alignment = alignment, Size = size, ArrayStride = size, MatrixStride = matrixStride };
}

public sealed class ShaderManifest
{
    public string EntryPointName { get; init; } = string.Empty;
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public string StorageLayout { get; init; } = ShaderStd430Layout.Standard;
    public IReadOnlyList<ShaderResourceManifest> Resources { get; init; } = [];

    public static ShaderManifest FromModule(ShaderIrModule module)
    {
        var resources = new List<ShaderResourceManifest>(module.Resources.Count);
        foreach (var resource in module.Resources)
        {
            var layout = resource.Layout ?? ShaderStd430Layout.ForGlslType(resource.GlslType);
            resources.Add(new ShaderResourceManifest
            {
                Name = resource.Name,
                ParameterName = resource.ParameterName,
                Category = resource.Category,
                Set = resource.Set,
                Binding = resource.Binding,
                GlslType = resource.GlslType,
                ReadOnly = resource.ReadOnly,
                Layout = ShaderStd430Layout.Standard,
                Offset = layout.Offset,
                Alignment = layout.Alignment,
                Size = layout.Size,
                ArrayStride = layout.ArrayStride,
                MatrixStride = layout.MatrixStride
            });
        }

        return new ShaderManifest
        {
            EntryPointName = module.EntryPointName,
            LocalSizeX = module.LocalSizeX,
            LocalSizeY = module.LocalSizeY,
            LocalSizeZ = module.LocalSizeZ,
            Resources = resources
        };
    }
}

public sealed class ShaderResourceManifest
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public bool ReadOnly { get; init; }
    public string Layout { get; init; } = ShaderStd430Layout.Standard;
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
}
