using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Two-stage trigonometric domain warp around a luminous center.</summary>
internal static class Example07_WarpedWorlds
{
    [FragmentShader]
    public static void WarpedWorlds(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        var warp = new float2(maths.sin(p.y * 4f + constants.Time), maths.cos(p.x * 3f - constants.Time * 0.7f));
        var q = p + 0.22f * warp;
        var radius = maths.length(q);
        var bands = 0.5f + 0.5f * maths.sin(q.x * 12f + q.y * 7f + constants.Time * 2f);
        var glow = maths.exp(-radius * radius * 2.2f);
        color = new float4(glow * (0.1f + 0.85f * bands), glow * 0.3f + 0.2f * bands, glow * (0.8f - 0.45f * bands), 1f);
    }
}
