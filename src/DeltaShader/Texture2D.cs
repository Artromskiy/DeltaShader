using System;

namespace Delta.Shader;

/// <summary>Opaque combined image sampler supplied by the graphics runtime.</summary>
public sealed class SampledTexture2D
{
    private SampledTexture2D()
    {
    }

    [ShaderIntrinsic("texture", ShaderStage.Compute, ShaderStage.Vertex, ShaderStage.Fragment)]
    public TColor Sample<TCoordinate, TColor>(TCoordinate coordinate)
        => throw new NotSupportedException("Sampled textures are compiler intrinsics.");
}
