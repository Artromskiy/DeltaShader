using Delta.Graphics.Semantics;
using Delta;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>
/// Explicit per-frame values used by the internal fragment fixtures.
/// </summary>
public struct GalleryConstants
{
    public float2 Resolution;
    public float Time;
}
[Interstage]
public struct GalleryVarying
{
    public Position Position;
}

public readonly struct GalleryFragmentContext
{
    [PushConstant]
    public readonly GalleryConstants Constants;
}

internal static class GalleryCoordinates
{
    public static float2 FragmentUv(float2 resolution) =>
        new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / resolution;

    public static float2 Centered(float2 resolution) => FragmentUv(resolution) * 2f - 1f;
}
