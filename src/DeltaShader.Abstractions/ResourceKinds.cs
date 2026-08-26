using System;

namespace Delta.Shader.Abstractions;

public enum ShaderResourceKind
{
    None = 0,
    Unknown = 1,
    StorageBuffer = 2,
    SampledTexture2D = 3
}

public static class ShaderResourceKindExtensions
{
    public static ShaderResourceKind ParseMetadataName(string? value)
        => value switch
        {
            "storage-buffer" => ShaderResourceKind.StorageBuffer,
            "sampled-texture" => ShaderResourceKind.SampledTexture2D,
            "none" => ShaderResourceKind.None,
            "unknown" => ShaderResourceKind.Unknown,
            _ => ShaderResourceKind.Unknown
        };

    public static string ToMetadataName(this ShaderResourceKind value)
        => value switch
        {
            ShaderResourceKind.None => "none",
            ShaderResourceKind.StorageBuffer => "storage-buffer",
            ShaderResourceKind.SampledTexture2D => "sampled-texture",
            _ => "unknown"
        };
}
