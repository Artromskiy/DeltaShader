using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Layered height bands suggesting a tiny procedural landscape.</summary>
internal static class Example25_FractalLand
{
    [FragmentShader]
    public static float4 FractalLand(in GalleryFragmentContext context)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var terrain = 0f;
        var amplitude = 0.55f;
        var frequency = 1.8f;
        for (var octave = 0f; octave < 4f; octave += 1f)
        {
            terrain += amplitude * maths.sin(p.x * frequency + octave * 1.7f + context.Constants.Time * 0.12f) * (0.5f + 0.5f * maths.cos(p.x * frequency * 0.37f));
            frequency = frequency * 1.9f;
            amplitude = amplitude * 0.48f;
        }
        var horizon = p.y - terrain * 0.18f + 0.1f;
        var land = 1f - maths.smoothstep(-0.025f, 0.025f, horizon);
        var snow = land * (0.5f + 0.5f * maths.sin(p.x * 14f + terrain * 10f));
        return new float4(0.04f + land * 0.16f + snow * 0.14f, 0.1f + land * 0.2f + snow * 0.22f, 0.22f + (1f - land) * 0.42f + snow * 0.26f, 1f);
    }
}
