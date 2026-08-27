using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Rotating polar sectors with a soft radial falloff.</summary>
internal static class Example09_RainbowSectors
{
    [FragmentShader]
    public static void RainbowSectors(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f)) + constants.Time * 0.4f;
        var radius = maths.length(p);
        var sector = 0.5f + 0.5f * maths.cos(angle * 8f);
        var red = 0.5f + 0.5f * maths.cos(angle + 0.0f);
        var green = 0.5f + 0.5f * maths.cos(angle + 2.094f);
        var blue = 0.5f + 0.5f * maths.cos(angle + 4.188f);
        var fade = maths.max(0f, 1f - radius);
        color = new float4(fade * red * (0.35f + 0.65f * sector), fade * green, fade * blue * (1.1f - sector * 0.4f), 1f);
    }
}
