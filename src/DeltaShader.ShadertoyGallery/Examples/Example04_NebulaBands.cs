using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Animated polar bands that suggest a compact nebula.</summary>
internal static class Example04_NebulaBands
{
    [FragmentShader]
    public static float4 NebulaBands(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var ribbon = 0.5f + 0.5f * maths.sin(angle * 5f + radius * 11f - context.Constants.Time * 1.7f);
        var haze = maths.exp(-radius * radius * 1.8f);
        var dust = 0.5f + 0.5f * maths.cos(radius * 42f - angle * 3f + context.Constants.Time);
        return new float4(haze * (0.15f + 0.75f * ribbon), haze * (0.1f + 0.45f * dust), haze * (0.3f + 0.65f * (1f - ribbon)), 1f);
    }
}
