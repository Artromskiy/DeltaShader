using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Three orbiting exponential blobs blended into a warm field.</summary>
internal static class Example12_MetaballGlow
{
    [FragmentShader]
    public static float4 MetaballGlow(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var energy = 0f;
        for (var ball = 0f; ball < 3f; ball += 1f)
        {
            var phase = context.Constants.Time * (0.7f + ball * 0.17f) + ball * 2.094f;
            var center = new float2(0.48f * maths.cos(phase), 0.38f * maths.sin(phase * 1.3f));
            var delta = p - center;
            var distanceSquared = maths.dot(delta, delta);
            energy += maths.exp(-distanceSquared * 12f);
        }
        var core = maths.clamp(energy, 0f, 1f);
        return new float4(core * (1f - 0.35f * core), core * 0.45f, 0.08f + core * 0.8f, 1f);
    }
}
