using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Sparse pulse nodes connected by a cheap animated field.</summary>
internal static class Example08_NeuronField
{
    [FragmentShader]
    public static void NeuronField(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var pulse = 0f;
        var connections = 0f;
        for (var i = 0f; i < 4f; i += 1f)
        {
            var phase = i * 1.73f + constants.Time * (0.6f + i * 0.08f);
            var node = new float2(0.55f * maths.cos(phase), 0.42f * maths.sin(phase * 1.21f));
            var distance = maths.length(p - node);
            pulse += maths.exp(-distance * distance * 95f) * (0.5f + 0.5f * maths.sin(phase * 3f));
            connections += maths.exp(-maths.abs(p.x * maths.sin(phase) - p.y * maths.cos(phase)) * 30f) * maths.exp(-distance * 1.5f);
        }
        color = new float4(0.02f + 0.8f * pulse, 0.06f + 0.25f * connections, 0.12f + 0.9f * connections, 1f);
    }
}
