using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader;

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
            "float16_t" => Scalar(2),
            "double" => Scalar(8),
            "bvec2" or "ivec2" or "uvec2" or "vec2" => Vector(8, 8),
            "bvec3" or "ivec3" or "uvec3" or "vec3" => Vector(16, 12),
            "bvec4" or "ivec4" or "uvec4" or "vec4" => Vector(16, 16),
            "f16vec2" => Vector(4, 4),
            "f16vec3" => Vector(8, 6),
            "f16vec4" => Vector(8, 8),
            "dvec2" => Vector(16, 16),
            "dvec3" => Vector(32, 24),
            "dvec4" => Vector(32, 32),
            "mat2" or "mat2x2" => Matrix(2, 2),
            "mat2x3" => Matrix(2, 3),
            "mat2x4" => Matrix(2, 4),
            "mat3x2" => Matrix(3, 2),
            "mat3" or "mat3x3" => Matrix(3, 3),
            "mat3x4" => Matrix(3, 4),
            "mat4x2" => Matrix(4, 2),
            "mat4x3" => Matrix(4, 3),
            "mat4" or "mat4x4" => Matrix(4, 4),
            "f16mat2" or "f16mat2x2" => Matrix(2, 2, 2),
            "f16mat2x3" => Matrix(2, 3, 2),
            "f16mat2x4" => Matrix(2, 4, 2),
            "f16mat3x2" => Matrix(3, 2, 2),
            "f16mat3" or "f16mat3x3" => Matrix(3, 3, 2),
            "f16mat3x4" => Matrix(3, 4, 2),
            "f16mat4x2" => Matrix(4, 2, 2),
            "f16mat4x3" => Matrix(4, 3, 2),
            "f16mat4" or "f16mat4x4" => Matrix(4, 4, 2),
            "dmat2" or "dmat2x2" => Matrix(2, 2, 8),
            "dmat2x3" => Matrix(2, 3, 8),
            "dmat2x4" => Matrix(2, 4, 8),
            "dmat3x2" => Matrix(3, 2, 8),
            "dmat3" or "dmat3x3" => Matrix(3, 3, 8),
            "dmat3x4" => Matrix(3, 4, 8),
            "dmat4x2" => Matrix(4, 2, 8),
            "dmat4x3" => Matrix(4, 3, 8),
            "dmat4" or "dmat4x4" => Matrix(4, 4, 8),
            _ => throw new ArgumentException($"Unsupported GLSL type '{glslType}'.", nameof(glslType))
        };
    }

    private static ShaderStd430Layout Scalar(uint size)
        => new() { Alignment = size, Size = size, ArrayStride = size };

    private static ShaderStd430Layout Vector(uint alignment, uint size)
        => new() { Alignment = alignment, Size = size, ArrayStride = alignment };

    private static ShaderStd430Layout Matrix(uint columns, uint rows, uint scalarSize = 4)
    {
        var matrixStride = (rows == 2 ? 2u : 4u) * scalarSize;
        var size = columns * matrixStride;
        return new() { Alignment = matrixStride, Size = size, ArrayStride = size, MatrixStride = matrixStride };
    }

    public static ShaderStd430Layout ForStruct(uint alignment, uint size)
        => new() { Alignment = alignment, Size = size, ArrayStride = size };
}

public sealed class ShaderManifest
{
    public ShaderStage Stage { get; init; } = ShaderStage.Compute;
    public string SourceEntryPointName { get; init; } = string.Empty;
    public string EntryPointName { get; init; } = string.Empty;
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public string StorageLayout { get; init; } = ShaderStd430Layout.Standard;
    public IReadOnlyList<ShaderVertexInputManifest> VertexInputs { get; init; } = [];
    public IReadOnlyList<ShaderVertexBufferBindingManifest> VertexBufferBindings { get; init; } = [];
    public IReadOnlyList<ShaderResourceManifest> Resources { get; init; } = [];
    public IReadOnlyList<ShaderInterfaceManifest> Inputs { get; init; } = [];
    public IReadOnlyList<ShaderInterfaceManifest> Outputs { get; init; } = [];
    public IReadOnlyList<ShaderPushConstantManifest> PushConstants { get; init; } = [];

    public static ShaderManifest FromModule(ShaderIrModule module)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        var resources = new List<ShaderResourceManifest>(module.Resources.Count);
        foreach (var resource in module.Resources)
        {
            var opaque = resource.Category == ShaderResourceKind.SampledTexture2D;
            var layout = opaque ? null : resource.Std430Layout;
            resources.Add(new ShaderResourceManifest
            {
                Name = resource.Name,
                ParameterName = resource.ParameterName,
                Category = resource.Category,
                Stage = module.Stage,
                Set = resource.Set,
                Binding = resource.Binding,
                GlslType = resource.GlslType,
                ReadOnly = resource.Access == ShaderResourceAccess.ReadOnly,
                Access = resource.Access,
                Layout = resource.Layout,
                Offset = layout?.Offset ?? 0,
                Alignment = layout?.Alignment ?? 0,
                Size = layout?.Size ?? 0,
                ArrayStride = layout?.ArrayStride ?? 0,
                MatrixStride = layout?.MatrixStride,
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
            Stage = module.Stage,
            SourceEntryPointName = string.IsNullOrWhiteSpace(module.SourceEntryPointName) ? module.EntryPointName : module.SourceEntryPointName,
            EntryPointName = module.EntryPointName,
            LocalSizeX = module.LocalSizeX,
            LocalSizeY = module.LocalSizeY,
            LocalSizeZ = module.LocalSizeZ,
            Resources = resources,
            Inputs = module.Inputs.Select(variable => new ShaderInterfaceManifest
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Builtin = variable.Builtin
            }).ToArray(),
            VertexInputs = module.VertexInputs.Select(variable => new ShaderVertexInputManifest
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Binding = variable.Binding,
                ByteOffset = variable.ByteOffset,
                InputRate = variable.InputRate,
                ByteSize = variable.ByteSize,
                Alignment = variable.Alignment,
                FormatHint = variable.FormatHint
            }).ToArray(),
            VertexBufferBindings = module.VertexBuffers.Select(binding => new ShaderVertexBufferBindingManifest
            {
                Binding = binding.Binding,
                Stride = binding.Stride,
                InputRate = binding.InputRate,
                Attributes = binding.Attributes.Select(attribute => new ShaderVertexInputManifest
                {
                    Name = attribute.Name,
                    ParameterName = attribute.ParameterName,
                    GlslName = attribute.GlslName,
                    GlslType = attribute.GlslType,
                    Location = attribute.Location,
                    Binding = attribute.Binding,
                    ByteOffset = attribute.ByteOffset,
                    InputRate = attribute.InputRate,
                    ByteSize = attribute.ByteSize,
                    Alignment = attribute.Alignment,
                    FormatHint = attribute.FormatHint
                }).ToArray()
            }).ToArray(),
            Outputs = module.Outputs.Select(variable => new ShaderInterfaceManifest
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Builtin = variable.Builtin
            }).ToArray(),
            PushConstants = module.PushConstants.Select(push => new ShaderPushConstantManifest
            {
                Name = push.Name,
                ParameterName = push.ParameterName,
                GlslType = push.GlslType,
                Alignment = push.Alignment,
                Size = push.Size,
                ArrayStride = push.ArrayStride,
                Members = push.Members.Select(ToManifestMember).ToArray()
            }).ToArray()
        };
    }

    public ShaderCompilationManifest ToBuildManifest(ShaderCompilationOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var resources = new List<ShaderCompilationResource>(Resources.Count);
        foreach (var resource in Resources)
        {
            resources.Add(new ShaderCompilationResource
            {
                Name = resource.Name,
                ParameterName = resource.ParameterName,
                Category = resource.Category.ToMetadataName(),
                Stage = resource.Stage,
                Set = resource.Set,
                Binding = resource.Binding,
                GlslType = resource.GlslType,
                ReadOnly = resource.ReadOnly,
                Access = resource.Access,
                Layout = resource.Layout,
                Offset = resource.Offset,
                Alignment = resource.Alignment,
                Size = resource.Size,
                ArrayStride = resource.ArrayStride,
                MatrixStride = resource.MatrixStride,
                Packing = resource.Category == ShaderResourceKind.SampledTexture2D
                    ? new ShaderCompilationPackingPlan { Scheme = "none", Strategy = "opaque-resource", Stride = 0 }
                    : new ShaderCompilationPackingPlan { Stride = resource.ArrayStride },
                Members = resource.Members.Select(member => new ShaderCompilationMember
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
                    Members = member.Members.Select(nested => new ShaderCompilationMember
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

        return new ShaderCompilationManifest
        {
            Stage = Stage,
            SourceEntryPointName = SourceEntryPointName,
            EntryPointName = "main",
            TargetProfile = options.Profile,
            GlslVersion = options.Glsl,
            SpirvVersion = options.Spirv,
            StorageLayout = StorageLayout,
            LocalSizeX = LocalSizeX,
            LocalSizeY = LocalSizeY,
            LocalSizeZ = LocalSizeZ,
            Resources = resources,
            Inputs = Inputs.Select(variable => new ShaderCompilationInterfaceVariable
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Builtin = variable.Builtin
            }).ToArray(),
            VertexInputs = VertexInputs.Select(variable => new ShaderCompilationVertexInput
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Binding = variable.Binding,
                ByteOffset = variable.ByteOffset,
                InputRate = variable.InputRate,
                ByteSize = variable.ByteSize,
                Alignment = variable.Alignment,
                FormatHint = variable.FormatHint
            }).ToArray(),
            VertexBufferBindings = VertexBufferBindings.Select(binding => new ShaderCompilationVertexBufferBinding
            {
                Binding = binding.Binding,
                Stride = binding.Stride,
                InputRate = binding.InputRate,
                Attributes = binding.Attributes.Select(attribute => new ShaderCompilationVertexInput
                {
                    Name = attribute.Name,
                    ParameterName = attribute.ParameterName,
                    GlslName = attribute.GlslName,
                    GlslType = attribute.GlslType,
                    Location = attribute.Location,
                    Binding = attribute.Binding,
                    ByteOffset = attribute.ByteOffset,
                    InputRate = attribute.InputRate,
                    ByteSize = attribute.ByteSize,
                    Alignment = attribute.Alignment,
                    FormatHint = attribute.FormatHint
                }).ToArray()
            }).ToArray(),
            Outputs = Outputs.Select(variable => new ShaderCompilationInterfaceVariable
            {
                Name = variable.Name,
                ParameterName = variable.ParameterName,
                GlslName = variable.GlslName,
                GlslType = variable.GlslType,
                Location = variable.Location,
                Builtin = variable.Builtin
            }).ToArray(),
            PushConstants = PushConstants.Select(push => new ShaderCompilationPushConstant
            {
                Name = push.Name,
                ParameterName = push.ParameterName,
                GlslType = push.GlslType,
                Alignment = push.Alignment,
                Size = push.Size,
                ArrayStride = push.ArrayStride,
                Members = push.Members.Select(ToBuildMember).ToArray()
            }).ToArray()
        };
    }

    private static ShaderResourceMemberManifest ToManifestMember(ShaderIrStructMember member)
        => new()
        {
            Name = member.Name,
            GlslName = member.GlslName,
            GlslType = member.GlslType,
            Offset = member.Offset,
            Alignment = member.Alignment,
            Size = member.Size,
            ArrayStride = member.ArrayStride,
            MatrixStride = member.MatrixStride,
            Members = member.Members.Select(ToManifestMember).ToArray()
        };

    private static ShaderCompilationMember ToBuildMember(ShaderIrStructMember member)
        => ToBuildMember(
            member.Name,
            member.GlslName,
            member.GlslType,
            member.Offset,
            member.Alignment,
            member.Size,
            member.ArrayStride,
            member.MatrixStride,
            member.Members.Select(ToBuildMember).ToArray());

    private static ShaderCompilationMember ToBuildMember(ShaderResourceMemberManifest member)
        => ToBuildMember(
            member.Name,
            member.GlslName,
            member.GlslType,
            member.Offset,
            member.Alignment,
            member.Size,
            member.ArrayStride,
            member.MatrixStride,
            member.Members.Select(ToBuildMember).ToArray());

    private static ShaderCompilationMember ToBuildMember(
        string name,
        string glslName,
        string glslType,
        uint offset,
        uint alignment,
        uint size,
        uint arrayStride,
        uint? matrixStride,
        IReadOnlyList<ShaderCompilationMember> members)
        => new()
        {
            Name = name,
            GlslName = glslName,
            GlslType = glslType,
            Offset = offset,
            Alignment = alignment,
            Size = size,
            ArrayStride = arrayStride,
            MatrixStride = matrixStride,
            HostRepresentation = glslType.StartsWith("bvec", StringComparison.Ordinal) || glslType == "bool"
                ? "bool32"
                : "std430",
            Members = members
        };
}

public sealed class ShaderInterfaceManifest
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public string? Builtin { get; init; }
}

public sealed class ShaderVertexInputManifest
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public uint Binding { get; init; }
    public uint ByteOffset { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public uint ByteSize { get; init; }
    public uint Alignment { get; init; }
    public string FormatHint { get; init; } = string.Empty;
}

public sealed class ShaderVertexBufferBindingManifest
{
    public uint Binding { get; init; }
    public uint Stride { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public IReadOnlyList<ShaderVertexInputManifest> Attributes { get; init; } = [];
}

public sealed class ShaderPushConstantManifest
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<ShaderResourceMemberManifest> Members { get; init; } = [];
}

public sealed class ShaderResourceManifest
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public ShaderResourceKind Category { get; init; } = ShaderResourceKind.Unknown;
    public ShaderStage Stage { get; init; }
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public bool ReadOnly { get; init; }
    public ShaderResourceAccess Access { get; init; } = ShaderResourceAccess.ReadWrite;
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
