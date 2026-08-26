using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Radial branch lights growing from a pulsing core.</summary>
internal static class Example20_EnergyPlant
{
    [FragmentShader]
    public static void EnergyPlant(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        var radius = maths.length(p);
        var core = maths.exp(-radius * radius * 28f);
        var branches = 0f;
        for (var branch = 0f; branch < 5f; branch += 1f)
        {
            var angle = branch * 1.257f + constants.Time * 0.25f;
            var axis = new float2(maths.cos(angle), maths.sin(angle));
            var across = maths.abs(p.x * axis.y - p.y * axis.x);
            var along = maths.dot(p, axis);
            branches += maths.exp(-across * 90f) * maths.exp(-maths.abs(along - 0.36f) * 9f);
        }
        color = new float4(0.05f + core * 0.9f, 0.12f + branches * 0.28f, 0.1f + branches * 0.75f + core * 0.25f, 1f);
    }
}
