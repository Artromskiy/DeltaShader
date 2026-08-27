using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Four rolling bands shaped into a low-cost hill field.</summary>
internal static class Example11_HillNoise
{
    [FragmentShader]
    public static float4 HillNoise(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var value = 0f;
        var weight = 0.55f;
        for (var layer = 0f; layer < 4f; layer += 1f)
        {
            var frequency = 2.5f + layer * 2.1f;
            value += weight * (0.5f + 0.5f * maths.sin(p.x * frequency + p.y * 3f + context.Constants.Time * (0.4f + layer * 0.1f)));
            weight = weight * 0.52f;
        }
        var slope = 0.5f + 0.5f * maths.sin(p.x * 5f - p.y * 2f + value * 4f);
        return new float4(0.05f + 0.35f * value, 0.12f + 0.7f * value * slope, 0.2f + 0.45f * (1f - value), 1f);
    }
}
