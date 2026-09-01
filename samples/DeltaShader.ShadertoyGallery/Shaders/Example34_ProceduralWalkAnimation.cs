using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Stick-figure-like limbs assembled from animated thin line fields.</summary>
internal static class Example34_ProceduralWalkAnimation
{
    [FragmentShader]
    public static float4 ProceduralWalkAnimation(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var cycle = context.Constants.Time * 2.2f;
        var stride = maths.sin(cycle);
        var torso = maths.exp(-maths.abs(p.x + 0.03f * stride) * 70f) * (1f - maths.smoothstep(0.35f, 0.72f, maths.abs(p.y)));
        var head = maths.exp(-maths.dot(p - new float2(0.03f + 0.03f * stride, 0.48f), p - new float2(0.03f + 0.03f * stride, 0.48f)) * 85f);
        var legs = 0f;
        for (var leg = 0f; leg < 2f; leg += 1f)
        {
            var side = leg * 2f - 1f;
            var swing = side * 0.23f * stride;
            var line = maths.abs((p.x - swing) * (0.45f + side * 0.08f) + (p.y + 0.3f) * (0.9f - side * 0.08f));
            var reach = 1f - maths.smoothstep(0.28f, 0.62f, maths.length(p - new float2(swing, -0.34f)));
            legs += maths.exp(-line * 95f) * reach;
        }
        var ground = maths.exp(-maths.abs(p.y + 0.62f) * 65f) * (0.35f + 0.65f * (0.5f + 0.5f * maths.sin(p.x * 9f)));
        return new float4(0.03f + legs * 0.2f + head * 0.14f, 0.08f + torso * 0.5f + legs * 0.12f, 0.15f + torso * 0.7f + ground * 0.18f, 1f);
    }
}
