using System;
using System.Collections.Generic;

namespace Delta.Shader.Abstractions;

public enum ShaderStage
{
    Compute
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

        Spirv = (byte[])spirv.Clone();
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public int FormatVersion => CurrentFormatVersion;
    public byte[] Spirv { get; }
    public ShaderAbiManifest Manifest { get; }
    public ShaderStage Stage => Manifest.Stage;
    public string EntryPoint => Manifest.EntryPointName;
}

public sealed class ShaderAbiManifest
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public ShaderStage Stage { get; set; } = ShaderStage.Compute;
    public string EntryPointName { get; set; } = string.Empty;
    public string TargetProfile { get; set; } = "vulkan1.2";
    public string GlslVersion { get; set; } = "460";
    public string SpirvVersion { get; set; } = "1.5";
    public string StorageLayout { get; set; } = "std430";
    public uint LocalSizeX { get; set; }
    public uint LocalSizeY { get; set; }
    public uint LocalSizeZ { get; set; }
    public IReadOnlyList<ShaderAbiResource> Resources { get; set; } = Array.Empty<ShaderAbiResource>();
}

public sealed class ShaderAbiResource
{
    public string Name { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public uint Set { get; set; }
    public uint Binding { get; set; }
    public string? GlslType { get; set; }
    public ShaderResourceAccess Access { get; set; } = ShaderResourceAccess.ReadWrite;
    public string Layout { get; set; } = "std430";
    public uint Offset { get; set; }
    public uint Alignment { get; set; }
    public uint Size { get; set; }
    public uint ArrayStride { get; set; }
    public uint? MatrixStride { get; set; }
}
