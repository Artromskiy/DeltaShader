using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Repeated 2D fold-and-invert steps inspired by Apollian fractals.</summary>
internal static class Example19_ApollianFold
{
    [FragmentShader]
    public static void ApollianFold(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        var q = p * 1.6f;
        var scale = 1f;
        for (var fold = 0f; fold < 5f; fold += 1f)
        {
            q = maths.abs(q) - new float2(0.52f, 0.48f);
            var radiusSquared = maths.max(maths.dot(q, q), 0.08f);
            q = q / radiusSquared - new float2(0.24f, 0.18f);
            scale = scale * 1.35f;
        }
        var distance = maths.length(q) / scale;
        var glow = maths.exp(-distance * 80f);
        var colorPhase = 0.5f + 0.5f * maths.sin(distance * 90f - constants.Time);
        color = new float4(glow * (0.5f + colorPhase), glow * 0.35f, glow * (1f - colorPhase) + 0.04f, 1f);
    }
}
