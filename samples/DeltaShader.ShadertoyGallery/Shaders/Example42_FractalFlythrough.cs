using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Perspective-like concentric tunnel rings with animated angular drift.</summary>
internal static class Example42_FractalFlythrough
{
    [FragmentShader]
    public static float4 FractalFlythrough(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var tunnel = 0f;
        for (var layer = 0f; layer < 6f; layer += 1f)
        {
            var depth = radius * (13f + layer * 5f) - context.Constants.Time * (1.2f + layer * 0.12f);
            var spoke = maths.sin(angle * (5f + layer) + depth * 0.35f);
            tunnel += maths.exp(-maths.abs(maths.sin(depth + spoke)) * 18f) / (layer + 1f);
        }
        var center = maths.exp(-radius * radius * 28f);
        return new float4(0.02f + tunnel * 0.13f, 0.04f + tunnel * 0.2f + center * 0.18f, 0.13f + tunnel * 0.52f + center * 0.45f, 1f);
    }
}
