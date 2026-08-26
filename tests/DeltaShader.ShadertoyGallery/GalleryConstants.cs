using Delta.Maths;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>
/// Explicit per-frame values used by the internal fragment fixtures.
/// </summary>
#pragma warning disable CA1051, CA1815 // Shader-visible fields and value semantics define the fixture push-constant ABI.
public struct GalleryConstants
{
    public float2 Resolution;
    public float Time;
}
#pragma warning restore CA1051, CA1815
