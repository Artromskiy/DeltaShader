using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Four bounded octaves feeding a second warped coordinate.</summary>
internal static class Example18_FbmWarp
{
    [FragmentShader]
    public static float4 FbmWarp(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var q = p + new float2(0.25f * maths.sin(p.y * 3f + context.Constants.Time), 0.2f * maths.cos(p.x * 4f - context.Constants.Time));
        var value = 0f;
        var weight = 0.5f;
        for (var octave = 0f; octave < 4f; octave += 1f)
        {
            value += weight * (0.5f + 0.5f * maths.sin(q.x * (3.2f + octave) + maths.cos(q.y * 2.7f - context.Constants.Time)));
            q = q * 1.85f + new float2(0.13f, -0.09f);
            weight = weight * 0.5f;
        }
        return new float4(0.05f + value * 0.6f, 0.08f + value * value * 0.5f, 0.18f + (1f - value) * 0.7f, 1f);
    }
}
