using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Rotating ring layers arranged as a compact procedural engine.</summary>
internal static class Example22_FractalEngine
{
    [FragmentShader]
    public static void FractalEngine(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var ring = 0f;
        for (var layer = 0f; layer < 4f; layer += 1f)
        {
            var phase = angle * (6f + layer * 2f) + radius * (18f - layer * 2f) - constants.Time * (0.8f + layer * 0.25f);
            ring += maths.exp(-maths.abs(maths.sin(phase)) * (10f + layer * 2f)) * (0.9f - layer * 0.12f);
        }
        var core = maths.exp(-radius * radius * 45f);
        color = new float4(0.02f + ring * 0.14f, 0.12f + ring * 0.38f + core * 0.3f, 0.22f + ring * 0.7f, 1f);
    }
}
