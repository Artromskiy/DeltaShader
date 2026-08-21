using System;
using System.Collections.Generic;

namespace Delta.Shader.Abstractions;

public enum ShaderStage
{
    Compute,
    Vertex,
    Fragment
}

public sealed class ShaderArtifact
{
    public const int CurrentFormatVersion = 1;

    public ShaderArtifact(byte[] spirv, ShaderAbiManifest manifest)
    {
        if (spirv is null || spirv.Length == 0)
        {
            throw new ArgumentException("SPIR-V artifact cannot be empty.", nameof(spirv));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (manifest.Version != ShaderAbiManifest.CurrentVersion)
        {
            throw new ArgumentException($"Unsupported shader ABI manifest version '{manifest.Version}'.", nameof(manifest));
        }

        Spirv = (byte[])spirv.Clone();
        Manifest = manifest;
    }

    public int FormatVersion => CurrentFormatVersion;
    public byte[] Spirv { get; }
    public ShaderAbiManifest Manifest { get; }
    public ShaderStage Stage => Manifest.Stage;
    public string EntryPoint => Manifest.EntryPointName;
}

public sealed class GraphicsShaderProgram
{
    public GraphicsShaderProgram(ShaderArtifact vertex, ShaderArtifact fragment)
    {
        Vertex = vertex ?? throw new ArgumentNullException(nameof(vertex));
        Fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
        if (vertex.Stage != ShaderStage.Vertex)
            throw new ArgumentException("The first artifact must contain a vertex stage.", nameof(vertex));
        if (fragment.Stage != ShaderStage.Fragment)
            throw new ArgumentException("The second artifact must contain a fragment stage.", nameof(fragment));
    }

    public ShaderArtifact Vertex { get; }
    public ShaderArtifact Fragment { get; }
}

public sealed class ShaderAbiManifest
{
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;
    public ShaderStage Stage { get; set; } = ShaderStage.Compute;
    public string SourceEntryPointName { get; set; } = string.Empty;
    public string EntryPointName { get; set; } = string.Empty;
    public string TargetProfile { get; set; } = "vulkan1.2";
    public string GlslVersion { get; set; } = "460";
    public string SpirvVersion { get; set; } = "1.5";
    public string StorageLayout { get; set; } = "std430";
    public uint LocalSizeX { get; set; }
    public uint LocalSizeY { get; set; }
    public uint LocalSizeZ { get; set; }
    public IReadOnlyList<ShaderAbiResource> Resources { get; set; } = Array.Empty<ShaderAbiResource>();
    public IReadOnlyList<ShaderAbiInterfaceVariable> Inputs { get; set; } = Array.Empty<ShaderAbiInterfaceVariable>();
    public IReadOnlyList<ShaderAbiVertexInput> VertexInputs { get; set; } = Array.Empty<ShaderAbiVertexInput>();
    public IReadOnlyList<ShaderAbiVertexBufferBinding> VertexBufferBindings { get; set; } = Array.Empty<ShaderAbiVertexBufferBinding>();
    public IReadOnlyList<ShaderAbiInterfaceVariable> Outputs { get; set; } = Array.Empty<ShaderAbiInterfaceVariable>();
    public IReadOnlyList<ShaderAbiPushConstant> PushConstants { get; set; } = Array.Empty<ShaderAbiPushConstant>();
}

public sealed class ShaderAbiInterfaceVariable
{
    public string Name { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string GlslName { get; set; } = string.Empty;
    public string GlslType { get; set; } = string.Empty;
    public uint Location { get; set; }
    public string? Builtin { get; set; }
}

public sealed class ShaderAbiVertexInput
{
    public string Name { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string GlslName { get; set; } = string.Empty;
    public string GlslType { get; set; } = string.Empty;
    public uint Location { get; set; }
    public uint Binding { get; set; }
    public uint ByteOffset { get; set; }
    public VertexInputRate InputRate { get; set; } = VertexInputRate.Vertex;
    public uint ByteSize { get; set; }
    public uint Alignment { get; set; }
    public string FormatHint { get; set; } = string.Empty;
}

public sealed class ShaderAbiVertexBufferBinding
{
    public uint Binding { get; set; }
    public uint Stride { get; set; }
    public VertexInputRate InputRate { get; set; } = VertexInputRate.Vertex;
    public IReadOnlyList<ShaderAbiVertexInput> Attributes { get; set; } = Array.Empty<ShaderAbiVertexInput>();
}

public sealed class ShaderAbiPushConstant
{
    public string Name { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string GlslType { get; set; } = string.Empty;
    public uint Alignment { get; set; }
    public uint Size { get; set; }
    public uint ArrayStride { get; set; }
    public IReadOnlyList<ShaderAbiMember> Members { get; set; } = Array.Empty<ShaderAbiMember>();
}

public sealed class ShaderAbiResource
{
    public string Name { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public ShaderStage Stage { get; set; }
    public uint Set { get; set; }
    public uint Binding { get; set; }
    public string? GlslType { get; set; }
    public bool ReadOnly { get; set; }
    public ShaderResourceAccess Access { get; set; } = ShaderResourceAccess.ReadWrite;
    public string Layout { get; set; } = "std430";
    public uint Offset { get; set; }
    public uint Alignment { get; set; }
    public uint Size { get; set; }
    public uint ArrayStride { get; set; }
    public uint? MatrixStride { get; set; }
    public IReadOnlyList<ShaderAbiMember> Members { get; set; } = Array.Empty<ShaderAbiMember>();
    public ShaderAbiPackingPlan Packing { get; set; } = new();
}

public sealed class ShaderAbiPackingPlan
{
    public string Scheme { get; set; } = "std430";
    public string Strategy { get; set; } = "std430-explicit-members";
    public bool DirectRawUploadAllowed { get; set; }
    public string BoolRepresentation { get; set; } = "uint32";
    public uint Stride { get; set; }
}

public sealed class ShaderAbiMember
{
    public string Name { get; set; } = string.Empty;
    public string GlslName { get; set; } = string.Empty;
    public string GlslType { get; set; } = string.Empty;
    public uint Offset { get; set; }
    public uint Alignment { get; set; }
    public uint Size { get; set; }
    public uint ArrayStride { get; set; }
    public uint? MatrixStride { get; set; }
    public string HostRepresentation { get; set; } = "std430";
    public IReadOnlyList<ShaderAbiMember> Members { get; set; } = Array.Empty<ShaderAbiMember>();
}
