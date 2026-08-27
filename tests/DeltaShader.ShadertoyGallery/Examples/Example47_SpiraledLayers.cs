using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Nested spiral shells formed from bounded angular bands.</summary>
internal static class Example47_SpiraledLayers
{
    [FragmentShader]
    public static float4 SpiraledLayers(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var layers = 0f;
        for (var layer = 0f; layer < 5f; layer += 1f)
        {
            var spiral = angle * (3f + layer * 0.8f) + (radius * 8f - (1f - radius) * 0.9f) * (1f + layer * 0.12f) - context.Constants.Time * (0.5f + layer * 0.1f);
            layers += maths.exp(-maths.abs(maths.sin(spiral)) * 13f) / (layer + 1f);
        }
        var center = maths.exp(-radius * radius * 50f);
        return new float4(0.03f + layers * 0.14f, 0.025f + layers * 0.22f + center * 0.18f, 0.11f + layers * 0.52f + center * 0.48f, 1f);
    }
}
