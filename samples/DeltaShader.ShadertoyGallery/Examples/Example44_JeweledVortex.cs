using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Rotating polygon-like facets arranged around a jewel-shaped core.</summary>
internal static class Example44_JeweledVortex
{
    [FragmentShader]
    public static float4 JeweledVortex(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var facets = 0f;
        for (var facet = 0f; facet < 5f; facet += 1f)
        {
            var phase = angle * (7f + facet) + radius * (10f + facet * 2f) - context.Constants.Time * (0.5f + facet * 0.17f);
            facets += (0.5f + 0.5f * maths.cos(phase)) * maths.exp(-radius * (1.4f + facet * 0.3f)) / (facet + 1f);
        }
        var jewel = maths.exp(-maths.abs(radius - 0.22f) * 38f);
        return new float4(0.07f + facets * 0.22f + jewel * 0.18f, 0.02f + facets * 0.08f + jewel * 0.35f, 0.18f + facets * 0.48f + jewel * 0.56f, 1f);
    }
}
