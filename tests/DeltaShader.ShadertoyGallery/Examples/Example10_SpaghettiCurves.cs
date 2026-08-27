using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Animated sine strands accumulated with bounded glow loops.</summary>
internal static class Example10_SpaghettiCurves
{
    [FragmentShader]
    public static void SpaghettiCurves(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var glow = new float3(0f, 0f, 0f);
        for (var strand = 0f; strand < 3f; strand += 1f)
        {
            var phase = strand * 2.1f + constants.Time;
            var curve = 0.38f * maths.sin(p.x * (4f + strand) + phase) + 0.15f * maths.cos(p.x * 9f - phase);
            var distance = maths.abs(p.y - curve - (strand - 1f) * 0.34f);
            var intensity = maths.exp(-distance * distance * 180f);
            glow = glow + new float3(intensity * (0.3f + strand * 0.2f), intensity * (1f - strand * 0.25f), intensity * (0.25f + (2f - strand) * 0.25f));
        }
        color = new float4(glow.x, glow.y, glow.z, 1f);
    }
}
