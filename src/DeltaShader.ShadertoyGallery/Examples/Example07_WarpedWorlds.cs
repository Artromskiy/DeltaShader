using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Two-stage trigonometric domain warp around a luminous center.</summary>
internal static class Example07_WarpedWorlds
{
    [FragmentShader]
    public static float4 WarpedWorlds(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var warp = new float2(maths.sin(p.y * 4f + context.Constants.Time), maths.cos(p.x * 3f - context.Constants.Time * 0.7f));
        var q = p + 0.22f * warp;
        var radius = maths.length(q);
        var bands = 0.5f + 0.5f * maths.sin(q.x * 12f + q.y * 7f + context.Constants.Time * 2f);
        var glow = maths.exp(-radius * radius * 2.2f);
        return new float4(glow * (0.1f + 0.85f * bands), glow * 0.3f + 0.2f * bands, glow * (0.8f - 0.45f * bands), 1f);
    }
}
