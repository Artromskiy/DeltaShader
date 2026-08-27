using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Seven inexpensive interference passes approximating a colored noise texture.</summary>
internal static class Example49_TasteOfNoise7
{
    [FragmentShader]
    public static void TasteOfNoise7(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var value = 0f;
        var weight = 0.5f;
        for (var layer = 0f; layer < 7f; layer += 1f)
        {
            var phase = maths.dot(p, new float2(3.1f + layer * 1.7f, 5.2f - layer * 0.63f)) + constants.Time * (0.18f + layer * 0.04f);
            value += weight * (0.5f + 0.5f * maths.sin(phase + maths.cos(phase * 1.73f)));
            p = p * 1.92f + new float2(0.17f, -0.11f);
            weight = weight * 0.53f;
        }
        color = new float4(0.04f + value * 0.22f, 0.06f + value * 0.38f, 0.12f + value * 0.62f, 1f);
    }
}
