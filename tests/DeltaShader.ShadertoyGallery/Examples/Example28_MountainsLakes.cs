using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Three sine-defined mountain ridges reflected into a calm lake.</summary>
internal static class Example28_MountainsLakes
{
    [FragmentShader]
    public static float4 MountainsLakes(in GalleryFragmentContext context)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var ridgeA = 0.28f + 0.12f * maths.sin(p.x * 2.4f + 0.4f) + 0.06f * maths.sin(p.x * 7f);
        var ridgeB = 0.02f + 0.15f * maths.sin(p.x * 3.8f - 0.8f) + 0.04f * maths.cos(p.x * 11f);
        var ridgeC = -0.18f + 0.09f * maths.sin(p.x * 5.2f + 1.5f);
        var upper = 1f - maths.smoothstep(ridgeA - 0.012f, ridgeA + 0.012f, p.y);
        var middle = (1f - upper) * (1f - maths.smoothstep(ridgeB - 0.012f, ridgeB + 0.012f, p.y));
        var lower = (1f - upper - middle) * (1f - maths.smoothstep(ridgeC - 0.012f, ridgeC + 0.012f, p.y));
        var ripple = 0.5f + 0.5f * maths.cos(p.x * 34f + context.Constants.Time * 0.7f);
        return new float4(0.03f + upper * 0.08f + middle * 0.08f + lower * 0.03f, 0.08f + upper * 0.13f + middle * 0.12f + ripple * 0.04f, 0.18f + (1f - upper) * 0.22f + ripple * 0.06f, 1f);
    }
}
