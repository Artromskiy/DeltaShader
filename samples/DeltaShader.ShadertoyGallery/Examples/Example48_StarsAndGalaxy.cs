using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Procedural star points distributed along a rotating galaxy arm.</summary>
internal static class Example48_StarsAndGalaxy
{
    [FragmentShader]
    public static float4 StarsAndGalaxy(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var stars = 0f;
        for (var star = 0f; star < 6f; star += 1f)
        {
            var starRadius = 0.12f + star * 0.13f;
            var armAngle = angle - starRadius * 4.5f - context.Constants.Time * 0.12f + star * 1.04f;
            var radial = maths.exp(-maths.abs(radius - starRadius) * 50f);
            stars += radial * maths.exp(-maths.abs(maths.sin(armAngle * 4f)) * 13f);
        }
        var core = maths.exp(-radius * radius * 18f);
        var dust = 0.5f + 0.5f * maths.sin(radius * 70f - angle * 8f);
        return new float4(0.015f + stars * 0.2f + core * 0.28f, 0.025f + stars * 0.16f + core * 0.18f, 0.07f + stars * 0.38f + core * 0.38f + dust * 0.025f, 1f);
    }
}
