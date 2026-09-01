using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Bounded inversion folds that grow a luminous fractal-like shell.</summary>
internal static class Example21_Fractal03
{
    [FragmentShader]
    public static float4 Fractal03(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var q = p;
        var glow = 0f;
        for (var fold = 0f; fold < 5f; fold += 1f)
        {
            q = new float2(maths.abs(q.x), maths.abs(q.y)) - new float2(0.34f, 0.28f);
            var radiusSquared = maths.dot(q, q);
            var inversion = 0.42f / (radiusSquared + 0.08f);
            q = q * inversion + new float2(0.08f * maths.sin(context.Constants.Time), -0.06f);
            glow += maths.exp(-maths.abs(maths.length(q) - 0.34f) * (18f + fold * 4f));
        }
        var vignette = 1f - maths.smoothstep(0.72f, 1.35f, maths.length(p));
        return new float4(0.12f + glow * 0.07f, 0.03f + glow * 0.18f, 0.2f + glow * 0.55f, 1f) * vignette;
    }
}
