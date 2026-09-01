using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Symmetric art-deco diamonds with alternating gold and blue insets.</summary>
internal static class Example43_GenerativeArtDeco4
{
    [FragmentShader]
    public static float4 GenerativeArtDeco4(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        p.x = maths.abs(p.x);
        p.y = maths.abs(p.y);
        var diamond = maths.abs(p.x) + maths.abs(p.y);
        var frame = maths.exp(-maths.abs(maths.sin(diamond * 14f + context.Constants.Time * 0.18f)) * 22f);
        var rays = maths.exp(-maths.abs(maths.sin((p.x - p.y) * 18f)) * 16f);
        var center = maths.exp(-maths.dot(p, p) * 18f);
        var gold = frame * (0.5f + 0.5f * maths.cos(p.x * 9f + p.y * 7f));
        return new float4(0.03f + gold * 0.5f + rays * 0.08f, 0.06f + gold * 0.27f + rays * 0.16f + center * 0.12f, 0.12f + rays * 0.42f + center * 0.5f, 1f);
    }
}
