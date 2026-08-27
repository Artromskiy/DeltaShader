using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Moving radial ripples with a soft center glow.</summary>
internal static class Example02_BumpyOrbit
{
    [FragmentShader]
    public static float4 BumpyOrbit(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var center = new float2(0.28f * maths.cos(context.Constants.Time), 0.18f * maths.sin(context.Constants.Time * 1.3f));
        var q = p - center;
        var radius = maths.length(q);
        var ripple = 0.5f + 0.5f * maths.cos(radius * 30f - context.Constants.Time * 4f);
        var glow = 1f / (1f + radius * radius * 18f);
        return new float4(glow * (0.2f + 0.8f * ripple), glow * 0.4f, glow * (1f - ripple), 1f);
    }
}
