using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Low-order Fourier lobes arranged around a warm heart-like silhouette.</summary>
internal static class Example26_IHeartFourier
{
    [FragmentShader]
    public static float4 IHeartFourier(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        p.y += 0.08f;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var outline = 0.43f + 0.1f * maths.cos(angle * 2f) - 0.04f * maths.cos(angle * 4f) + 0.025f * maths.sin(angle * 7f + context.Constants.Time);
        var fill = 1f - maths.smoothstep(outline - 0.018f, outline + 0.018f, radius);
        var rim = maths.exp(-maths.abs(radius - outline) * 55f);
        var pulse = 0.75f + 0.25f * maths.sin(context.Constants.Time * 3f);
        return new float4(0.35f + fill * 0.5f + rim * 0.2f, 0.025f + fill * 0.07f, 0.06f + fill * 0.12f + rim * 0.28f, 1f) * pulse;
    }
}
