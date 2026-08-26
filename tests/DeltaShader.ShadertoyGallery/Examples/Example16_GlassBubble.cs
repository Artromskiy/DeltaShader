using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Rim, body, and highlight fields for a glass-like bubble.</summary>
internal static class Example16_GlassBubble
{
    [FragmentShader]
    public static void GlassBubble(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        var radius = maths.length(p);
        var body = 1f - maths.smoothStep(0.42f, 0.5f, radius);
        var rim = maths.exp(-maths.abs(radius - 0.43f) * 65f);
        var highlightPoint = new float2(-0.15f, 0.16f + 0.04f * maths.sin(constants.Time));
        var highlight = maths.exp(-maths.dot(p - highlightPoint, p - highlightPoint) * 150f);
        color = new float4(0.04f + rim * 0.45f + highlight, 0.16f + body * 0.22f + rim * 0.5f, 0.22f + body * 0.65f + highlight * 0.6f, 1f);
    }
}
