using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Concentric radio waves crossed with slowly rotating interference.</summary>
internal static class Example24_FractalRadioBase
{
    [FragmentShader]
    public static float4 FractalRadioBase(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var waves = 0f;
        for (var band = 0f; band < 5f; band += 1f)
        {
            var frequency = 8f + band * 5f;
            var wave = 0.5f + 0.5f * maths.cos(radius * frequency - context.Constants.Time * (1.1f + band * 0.14f) + angle * (band + 1f));
            waves += wave * (0.55f / (band + 1f));
        }
        var halo = maths.exp(-radius * radius * 2.4f);
        return new float4(0.03f + waves * 0.24f + halo * 0.12f, 0.07f + waves * 0.35f, 0.18f + waves * 0.5f + halo * 0.35f, 1f);
    }
}
