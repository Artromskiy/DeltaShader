using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>
/// Concentric animated interference bands, independently recreated from the
/// high-level idea of ShaderToy's procedural/radial examples.
/// </summary>
internal static class Example01_SolarInterference
{
    [FragmentShader]
    public static void SolarInterference(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var radius = maths.length(p);
        var bands = 0.5f + 0.5f * maths.cos(18f * radius - constants.Time * 2f);
        var glow = 1f / (1f + 4f * radius * radius);
        var red = glow * (0.35f + 0.65f * bands);
        var green = glow * (0.12f + 0.48f * (1f - bands));
        var blue = glow * (0.08f + 0.55f * bands);
        color = new float4(red, green, blue, 1f);
    }
}
