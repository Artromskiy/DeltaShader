using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Two touching circle fields with a smooth shared silhouette.</summary>
internal static class Example15_TouchingOrbs
{
    [FragmentShader]
    public static void TouchingOrbs(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        var left = maths.length(p - new float2(-0.28f, 0f)) - 0.3f;
        var right = maths.length(p - new float2(0.28f, 0f)) - 0.3f;
        var field = maths.min(left, right);
        var fill = 1f - maths.smoothStep(-0.01f, 0.01f, field);
        var seam = maths.exp(-maths.abs(left - right) * 30f) * fill;
        color = new float4(0.06f + fill * 0.25f + seam * 0.5f, 0.1f + fill * 0.5f, 0.18f + fill * 0.75f, 1f);
    }
}
