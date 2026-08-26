using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Compact scalar reaction-diffusion-inspired update iterations.</summary>
internal static class Example36_LearningReactionDiffusion
{
    [FragmentShader]
    public static void LearningReactionDiffusion(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (fragmentCoord / constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var activator = 0.5f + 0.5f * maths.sin(p.x * 3f + constants.Time * 0.25f);
        var inhibitor = 0.5f + 0.5f * maths.cos(p.y * 4f - constants.Time * 0.18f);
        for (var iteration = 0f; iteration < 5f; iteration += 1f)
        {
            var neighborhood = 0.5f + 0.5f * maths.sin((p.x + activator * 0.3f) * (5f + iteration) + (p.y - inhibitor * 0.2f) * 3f);
            var reaction = activator * activator * inhibitor;
            activator = maths.clamp(activator + 0.18f * (neighborhood - reaction), 0f, 1f);
            inhibitor = maths.clamp(inhibitor + 0.13f * (activator - inhibitor * 0.7f), 0f, 1f);
        }
        color = new float4(0.025f + activator * 0.22f, 0.04f + activator * 0.52f, 0.1f + inhibitor * 0.54f, 1f);
    }
}
