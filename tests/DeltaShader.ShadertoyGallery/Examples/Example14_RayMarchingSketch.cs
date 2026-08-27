using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A bounded 2D sphere-tracing-style accumulation sketch.</summary>
internal static class Example14_RayMarchingSketch
{
    [FragmentShader]
    public static void RayMarchingSketch(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var travel = 0f;
        var glow = 0f;
        for (var step = 0f; step < 8f; step += 1f)
        {
            var samplePoint = p - new float2(0f, travel);
            var distance = maths.length(samplePoint) - 0.28f - 0.05f * maths.sin(samplePoint.x * 10f + constants.Time);
            glow += maths.exp(-maths.abs(distance) * 24f);
            travel += maths.max(distance, 0.025f) * 0.18f;
        }
        color = new float4(glow * 0.65f, glow * 0.25f, 0.08f + glow * 0.9f, 1f);
    }
}
