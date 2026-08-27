using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Animated branch strokes built from repeated polar line distances.</summary>
internal static class Example30_DancyTreeDoodle
{
    [FragmentShader]
    public static float4 DancyTreeDoodle(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var trunk = maths.exp(-maths.abs(p.x + 0.08f * maths.sin(p.y * 8f + context.Constants.Time)) * 55f) * (1f - maths.smoothStep(0.55f, 0.9f, maths.abs(p.y)));
        var branches = 0f;
        for (var branch = 0f; branch < 5f; branch += 1f)
        {
            var level = -0.45f + branch * 0.2f;
            var span = 0.2f + (branch + 1f) * 0.09f;
            var sway = 0.08f * maths.sin(context.Constants.Time * 1.4f + branch);
            var line = maths.abs((p.x - sway) * maths.cos(branch * 0.5f) + (p.y - level) * maths.sin(branch * 0.5f));
            var extent = 1f - maths.smoothStep(span, span + 0.08f, maths.abs(p.x));
            branches += maths.exp(-line * 85f) * extent;
        }
        var crown = maths.exp(-maths.dot(p - new float2(0f, 0.5f), p - new float2(0f, 0.5f)) * 8f);
        return new float4(0.015f + branches * 0.08f, 0.04f + trunk * 0.23f + branches * 0.18f, 0.025f + trunk * 0.08f + branches * 0.05f + crown * 0.16f, 1f);
    }
}
