using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Sine-displaced grid lines with a depth-like blue tint.</summary>
internal static class Example17_DisplacedGrid
{
    [FragmentShader]
    public static float4 DisplacedGrid(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var displacedX = p.x + 0.16f * maths.sin(p.y * 9f + context.Constants.Time);
        var displacedY = p.y + 0.16f * maths.cos(p.x * 8f - context.Constants.Time * 0.8f);
        var lineX = maths.exp(-maths.abs(maths.sin(displacedX * 18f)) * 18f);
        var lineY = maths.exp(-maths.abs(maths.sin(displacedY * 18f)) * 18f);
        var light = maths.clamp(lineX + lineY, 0f, 1f);
        return new float4(0.04f + light * 0.24f, 0.08f + light * 0.5f, 0.16f + light * 0.75f, 1f);
    }
}
