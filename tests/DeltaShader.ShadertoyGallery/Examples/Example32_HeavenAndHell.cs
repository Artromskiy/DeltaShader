using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Contrasting cool and warm hemispheres joined by a bright horizon.</summary>
internal static class Example32_HeavenAndHell
{
    [FragmentShader]
    public static void HeavenAndHell(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var separator = 0.12f * maths.sin(p.x * 3.4f + constants.Time * 0.45f);
        var heaven = 1f - maths.smoothStep(separator - 0.02f, separator + 0.02f, p.y);
        var hell = 1f - heaven;
        var embers = 0.5f + 0.5f * maths.sin(p.x * 16f - p.y * 11f - constants.Time * 1.4f);
        var seam = maths.exp(-maths.abs(p.y - separator) * 50f);
        color = new float4(0.08f * heaven + (0.35f + embers * 0.25f) * hell + seam * 0.85f, 0.18f * heaven + (0.035f + embers * 0.08f) * hell + seam * 0.35f, 0.36f * heaven + (0.015f + embers * 0.02f) * hell + seam * 0.05f, 1f);
        color = color * (0.75f + 0.25f * (1f - maths.smoothStep(0.65f, 1.25f, maths.length(p))));
    }
}
