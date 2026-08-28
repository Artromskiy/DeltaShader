using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Dark rotating rings recede toward a small blue point of light.</summary>
internal static class Example33_Oblivion
{
    [FragmentShader]
    public static float4 Oblivion(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var rings = 0f;
        for (var ring = 0f; ring < 5f; ring += 1f)
        {
            var phase = radius * (20f + ring * 7f) - angle * (3f + ring) - context.Constants.Time * (0.4f + ring * 0.13f);
            rings += maths.exp(-maths.abs(maths.sin(phase)) * 14f) / (ring + 1f);
        }
        var singularity = maths.exp(-radius * radius * 80f);
        var vignette = 1f - maths.smoothstep(0.5f, 1.3f, radius);
        return new float4(0.005f + rings * 0.03f + singularity * 0.02f, 0.008f + rings * 0.06f + singularity * 0.18f, 0.02f + rings * 0.12f + singularity * 0.58f, 1f) * vignette;
    }
}
