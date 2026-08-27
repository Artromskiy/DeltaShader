using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Animated color-channel ramps useful for testing scalar/vector lowering.</summary>
internal static class Example39_ColorProcessing
{
    [FragmentShader]
    public static float4 ColorProcessing(in GalleryFragmentContext context)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var red = 0.5f + 0.5f * maths.sin(p.x * 4f + context.Constants.Time);
        var green = 0.5f + 0.5f * maths.sin(p.y * 5f - context.Constants.Time * 0.7f + 2.1f);
        var blue = 0.5f + 0.5f * maths.sin((p.x + p.y) * 3f + context.Constants.Time * 0.4f + 4.2f);
        var source = new float3(red, green, blue);
        var processed = new float3(source.x * 0.8f + source.y * 0.15f, source.y * 0.72f + source.z * 0.2f, source.z * 0.9f + source.x * 0.12f);
        var contrast = 0.72f + 0.28f * maths.cos(maths.length(p) * 5f - context.Constants.Time);
        return new float4(processed.x * contrast, processed.y * contrast, processed.z * contrast, 1f);
    }
}
