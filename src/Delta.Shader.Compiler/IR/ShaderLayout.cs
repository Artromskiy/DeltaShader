using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader.Abstractions;

namespace Delta.Shader.Compiler.IR;

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

    public static ShaderStd430Layout ForStruct(uint alignment, uint size)
        => new() { Alignment = alignment, Size = size, ArrayStride = size };
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
                MatrixStride = layout.MatrixStride,
                Members = resource.Members.Select(member => new ShaderResourceMemberManifest
                {
                    Name = member.Name,
                    GlslName = member.GlslName,
                    GlslType = member.GlslType,
                    Offset = member.Offset,
                    Alignment = member.Alignment,
                    Size = member.Size,
                    ArrayStride = member.ArrayStride,
                    MatrixStride = member.MatrixStride,
                    Members = member.Members.Select(nested => new ShaderResourceMemberManifest
                    {
                        Name = nested.Name,
                        GlslName = nested.GlslName,
                        GlslType = nested.GlslType,
                        Offset = nested.Offset,
                        Alignment = nested.Alignment,
                        Size = nested.Size,
                        ArrayStride = nested.ArrayStride,
                        MatrixStride = nested.MatrixStride
                    }).ToArray()
                }).ToArray()
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

    public ShaderAbiManifest ToAbiManifest(ShaderCompilationOptions options)
    {
        var resources = new List<ShaderAbiResource>(Resources.Count);
        foreach (var resource in Resources)
        {
            resources.Add(new ShaderAbiResource
            {
                Name = resource.Name,
                ParameterName = resource.ParameterName,
                Category = resource.Category,
                Set = resource.Set,
                Binding = resource.Binding,
                GlslType = resource.GlslType,
                Access = resource.ReadOnly ? ShaderResourceAccess.ReadOnly : ShaderResourceAccess.ReadWrite,
                Layout = resource.Layout,
                Offset = resource.Offset,
                Alignment = resource.Alignment,
                Size = resource.Size,
                ArrayStride = resource.ArrayStride,
                MatrixStride = resource.MatrixStride,
                Packing = new ShaderAbiPackingPlan
                {
                    Stride = resource.ArrayStride
                },
                Members = resource.Members.Select(member => new ShaderAbiMember
                {
                    Name = member.Name,
                    GlslName = member.GlslName,
                    GlslType = member.GlslType,
                    Offset = member.Offset,
                    Alignment = member.Alignment,
                    Size = member.Size,
                    ArrayStride = member.ArrayStride,
                    MatrixStride = member.MatrixStride,
                    HostRepresentation = member.GlslType.StartsWith("bvec", StringComparison.Ordinal) || member.GlslType == "bool"
                        ? "bool32"
                        : "std430",
                    Members = member.Members.Select(nested => new ShaderAbiMember
                    {
                        Name = nested.Name,
                        GlslName = nested.GlslName,
                        GlslType = nested.GlslType,
                        Offset = nested.Offset,
                        Alignment = nested.Alignment,
                        Size = nested.Size,
                        ArrayStride = nested.ArrayStride,
                        MatrixStride = nested.MatrixStride,
                        HostRepresentation = nested.GlslType.StartsWith("bvec", StringComparison.Ordinal) || nested.GlslType == "bool"
                            ? "bool32"
                            : "std430"
                    }).ToArray()
                }).ToArray()
            });
        }

        return new ShaderAbiManifest
        {
            Version = ShaderAbiManifest.CurrentVersion,
            Stage = ShaderStage.Compute,
            SourceEntryPointName = EntryPointName,
            EntryPointName = "main",
            TargetProfile = options.Profile,
            GlslVersion = options.Glsl,
            SpirvVersion = options.Spirv,
            StorageLayout = StorageLayout,
            LocalSizeX = LocalSizeX,
            LocalSizeY = LocalSizeY,
            LocalSizeZ = LocalSizeZ,
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
    public IReadOnlyList<ShaderResourceMemberManifest> Members { get; init; } = [];
}

public sealed class ShaderResourceMemberManifest
{
    public string Name { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public IReadOnlyList<ShaderResourceMemberManifest> Members { get; init; } = [];
}
