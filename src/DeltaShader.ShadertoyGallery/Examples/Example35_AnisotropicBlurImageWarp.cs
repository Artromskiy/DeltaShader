using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A texture-free anisotropic blur analogue over a warped scalar field.</summary>
internal static class Example35_AnisotropicBlurImageWarp
{
    [FragmentShader]
    public static float4 AnisotropicBlurImageWarp(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var warp = new float2(0.18f * maths.sin(p.y * 5f + context.Constants.Time), 0.16f * maths.cos(p.x * 4f - context.Constants.Time * 0.7f));
        var q = p + warp;
        var blurred = 0f;
        var totalWeight = 0f;
        for (var sampleIndex = 0f; sampleIndex < 5f; sampleIndex += 1f)
        {
            var offset = (sampleIndex - 2f) * 0.08f;
            var samplePoint = new float2(q.x + offset * (0.7f + 0.3f * maths.sin(context.Constants.Time)), q.y + offset * 0.18f);
            var signal = 0.5f + 0.5f * maths.sin(samplePoint.x * 13f + maths.cos(samplePoint.y * 8f));
            var weight = 1f - maths.abs(sampleIndex - 2f) * 0.28f;
            blurred += signal * weight;
            totalWeight += weight;
        }
        var value = blurred / totalWeight;
        var edge = maths.exp(-maths.abs(value - 0.5f) * 13f);
        return new float4(0.04f + value * 0.18f, 0.07f + value * 0.35f + edge * 0.08f, 0.16f + value * 0.58f, 1f);
    }
}
