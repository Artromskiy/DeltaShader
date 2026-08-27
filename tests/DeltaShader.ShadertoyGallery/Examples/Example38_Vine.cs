using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A pair of curling tendrils with small leaf-like pulses.</summary>
internal static class Example38_Vine
{
    [FragmentShader]
    public static float4 Vine(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var stem = maths.exp(-maths.abs(p.x - 0.18f * maths.sin(p.y * 4f + context.Constants.Time * 0.5f)) * 48f) * (1f - maths.smoothStep(0.7f, 1.1f, maths.abs(p.y)));
        var leaves = 0f;
        for (var leaf = 0f; leaf < 5f; leaf += 1f)
        {
            var y = -0.64f + leaf * 0.3f;
            var x = 0.18f * maths.sin(y * 4f + context.Constants.Time * 0.5f);
            var leafPoint = new float2(x + 0.22f * maths.sin(leaf * 2.1f), y);
            var distance = maths.length(p - leafPoint);
            leaves += maths.exp(-distance * distance * 140f);
        }
        var tendril = maths.exp(-maths.abs(p.y - 0.3f * maths.sin(p.x * 8f + context.Constants.Time)) * 65f) * (1f - maths.smoothStep(0.45f, 0.95f, maths.abs(p.x)));
        return new float4(0.015f + stem * 0.05f + leaves * 0.08f, 0.05f + stem * 0.32f + leaves * 0.2f, 0.025f + stem * 0.1f + tendril * 0.25f + leaves * 0.06f, 1f);
    }
}
