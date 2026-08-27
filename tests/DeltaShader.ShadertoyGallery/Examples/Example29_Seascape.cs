using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Small bounded wave stack for a blue animated seascape.</summary>
internal static class Example29_Seascape
{
    [FragmentShader]
    public static void Seascape(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var height = 0f;
        var weight = 0.5f;
        for (var wave = 0f; wave < 5f; wave += 1f)
        {
            var direction = new float2(maths.cos(wave * 1.7f), maths.sin(wave * 1.7f));
            var phase = maths.dot(p, direction) * (4f + wave * 2.3f) + constants.Time * (0.45f + wave * 0.12f);
            height += weight * (0.5f + 0.5f * maths.sin(phase));
            weight = weight * 0.55f;
        }
        var horizon = 0.02f + (height - 0.4f) * 0.35f;
        var water = 1f - maths.smoothStep(horizon - 0.02f, horizon + 0.02f, p.y);
        var foam = maths.exp(-maths.abs(p.y - horizon) * 55f);
        color = new float4(0.03f + water * 0.02f + foam * 0.5f, 0.08f + water * 0.25f + foam * 0.35f, 0.18f + water * 0.45f + foam * 0.18f, 1f);
    }
}
