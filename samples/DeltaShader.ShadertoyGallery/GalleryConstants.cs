using Delta.Maths;

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
    [Interstage]
    public readonly GalleryVarying Varying;

    [PushConstant]
    public readonly GalleryConstants Constants;
}
