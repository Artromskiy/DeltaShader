using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A sun disk, moving corona, and a dark planetary silhouette.</summary>
internal static class Example40_Day94
{
    [FragmentShader]
    public static float4 Day94(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var sunCenter = new float2(-0.22f, 0.12f + 0.05f * maths.sin(context.Constants.Time * 0.2f));
        var sunDistance = maths.length(p - sunCenter);
        var sun = 1f - maths.smoothStep(0.17f, 0.19f, sunDistance);
        var corona = maths.exp(-sunDistance * sunDistance * 12f);
        var planetCenter = new float2(0.2f, -0.03f);
        var planet = 1f - maths.smoothStep(0.24f, 0.27f, maths.length(p - planetCenter));
        var rays = 0.5f + 0.5f * maths.sin((p.x - p.y) * 32f + context.Constants.Time * 0.8f);
        var horizon = 1f - maths.smoothStep(0.35f, 0.95f, maths.abs(p.y + 0.55f));
        return new float4(0.03f + corona * 0.42f + sun * 0.45f, 0.05f + corona * 0.25f + sun * 0.4f, 0.12f + corona * 0.08f + sun * 0.18f, 1f);
        color = color * (1f - planet * 0.92f) + new float4(0.03f, 0.06f, 0.11f, 1f) * planet;
        color = color + new float4(horizon * rays * 0.05f, horizon * rays * 0.035f, horizon * rays * 0.02f, 0f);
    }
}
