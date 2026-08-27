using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Texture-free refraction proxy using a warped layered color field.</summary>
internal static class Example46_SimpleRefractionTest
{
    [FragmentShader]
    public static float4 SimpleRefractionTest(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var normalProxy = new float2(p.x, p.y) / (radius + 0.08f);
        var refracted = p + normalProxy * (0.12f + 0.06f * maths.sin(context.Constants.Time));
        var layerA = 0.5f + 0.5f * maths.sin(refracted.x * 11f + refracted.y * 4f);
        var layerB = 0.5f + 0.5f * maths.cos(refracted.y * 15f - refracted.x * 3f);
        var interfaceGlow = maths.exp(-maths.abs(radius - 0.48f) * 45f);
        var bubble = 1f - maths.smoothStep(0.45f, 0.5f, radius);
        return new float4(0.025f + layerA * 0.11f + interfaceGlow * 0.18f, 0.08f + layerB * 0.28f + interfaceGlow * 0.32f, 0.18f + (layerA + layerB) * 0.24f + bubble * 0.18f, 1f);
    }
}
