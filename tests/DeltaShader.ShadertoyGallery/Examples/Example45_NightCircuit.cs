using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Orthogonal circuit traces with moving pulses on a night-blue board.</summary>
internal static class Example45_NightCircuit
{
    [FragmentShader]
    public static void NightCircuit(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = fragmentCoord / constants.Resolution;
        var p = uv * 2f - new float2(1f, 1f);
        p.x = p.x * constants.Resolution.x / constants.Resolution.y;
        var horizontal = maths.exp(-maths.abs(maths.sin(p.y * 9f + p.x * 2f)) * 28f);
        var vertical = maths.exp(-maths.abs(maths.sin(p.x * 11f - p.y * 1.5f)) * 28f);
        var pulseA = 0.5f + 0.5f * maths.sin(p.x * 17f - constants.Time * 2.3f);
        var pulseB = 0.5f + 0.5f * maths.cos(p.y * 19f + constants.Time * 1.7f);
        var nodes = 0f;
        for (var node = 0f; node < 4f; node += 1f)
        {
            var center = new float2(-0.58f + node * 0.38f, 0.24f * maths.sin(node * 2.7f));
            nodes += maths.exp(-maths.dot(p - center, p - center) * 95f);
        }
        var trace = maths.clamp(horizontal * (0.25f + pulseA * 0.75f) + vertical * (0.2f + pulseB * 0.8f) + nodes, 0f, 1.5f);
        color = new float4(0.01f + trace * 0.05f, 0.025f + trace * 0.18f, 0.08f + trace * 0.45f, 1f);
    }
}
