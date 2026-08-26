using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Droplets and thin spill trails spread across a dark surface.</summary>
internal static class Example37_Spilled
{
    [FragmentShader]
    public static void Spilled(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var drops = 0f;
        var trails = 0f;
        for (var drop = 0f; drop < 5f; drop += 1f)
        {
            var phase = drop * 1.37f;
            var center = new float2(-0.58f + drop * 0.28f + 0.06f * maths.sin(constants.Time * 0.4f + phase), 0.2f * maths.cos(phase * 2f) - 0.12f);
            var delta = p - center;
            drops += maths.exp(-maths.dot(delta, delta) * (80f - drop * 5f));
            var trailDistance = maths.abs(delta.y + 0.28f * maths.sin(delta.x * 8f + phase));
            trails += maths.exp(-trailDistance * 70f) * (1f - maths.smoothStep(0.12f, 0.8f, maths.abs(delta.x)));
        }
        var sheen = 0.5f + 0.5f * maths.sin(p.x * 15f + p.y * 4f);
        color = new float4(0.025f + drops * 0.32f + trails * 0.08f, 0.04f + drops * 0.15f + trails * 0.18f, 0.08f + drops * 0.05f + trails * 0.42f + sheen * 0.03f, 1f);
    }
}
