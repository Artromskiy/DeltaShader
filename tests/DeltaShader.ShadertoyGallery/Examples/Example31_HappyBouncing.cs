using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Four softly glowing discs move on independent bounded arcs.</summary>
internal static class Example31_HappyBouncing
{
    [FragmentShader]
    public static float4 HappyBouncing(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var glow = 0f;
        var colorMix = new float3(0.02f, 0.05f, 0.12f);
        for (var ball = 0f; ball < 4f; ball += 1f)
        {
            var phase = context.Constants.Time * (1.2f + ball * 0.17f) + ball * 1.57f;
            var center = new float2(-0.62f + ball * 0.4f + 0.08f * maths.sin(phase), -0.2f + 0.34f * maths.abs(maths.sin(phase * 0.83f)));
            var distance = maths.length(p - center);
            var light = maths.exp(-distance * distance * 70f);
            glow += light;
            colorMix += new float3(0.14f + ball * 0.03f, 0.07f + ball * 0.05f, 0.2f - ball * 0.025f) * light;
        }
        return new float4(colorMix.x + glow * 0.02f, colorMix.y + glow * 0.04f, colorMix.z + glow * 0.08f, 1f);
    }
}
