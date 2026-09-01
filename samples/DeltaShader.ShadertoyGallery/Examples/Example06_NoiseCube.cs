using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Small trigonometric noise stack with a cube-like value envelope.</summary>
internal static class Example06_NoiseCube
{
    [FragmentShader]
    public static float4 NoiseCube(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var q = p;
        var value = 0f;
        var weight = 0.5f;
        for (var octave = 0f; octave < 4f; octave += 1f)
        {
            value += weight * (0.5f + 0.5f * maths.sin(q.x * 3.1f + maths.cos(q.y * 2.4f + context.Constants.Time)));
            q = q * 1.9f + new float2(0.17f, -0.11f);
            weight = weight * 0.5f;
        }
        var cube = 1f - maths.max(maths.abs(p.x), maths.abs(p.y));
        return new float4(value * 0.45f, value * cube * 0.75f, value * 0.95f, 1f);
    }
}
