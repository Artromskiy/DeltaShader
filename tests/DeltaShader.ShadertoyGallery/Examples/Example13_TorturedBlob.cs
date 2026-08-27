using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A radially warped blob with a bright animated edge.</summary>
internal static class Example13_TorturedBlob
{
    [FragmentShader]
    public static void TorturedBlob(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var radius = maths.length(p);
        var angle = maths.atan(p.y / (maths.abs(p.x) + 0.001f));
        var boundary = 0.48f + 0.1f * maths.sin(angle * 7f + constants.Time * 1.4f);
        var shell = maths.exp(-maths.abs(radius - boundary) * 45f);
        var fill = 1f - maths.smoothStep(boundary - 0.02f, boundary + 0.02f, radius);
        color = new float4(0.08f + shell * 0.85f, 0.12f + fill * 0.35f, 0.2f + shell * 0.65f, 1f);
    }
}
