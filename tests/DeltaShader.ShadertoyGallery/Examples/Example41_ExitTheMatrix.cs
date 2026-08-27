using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Rain-like columns of luminous glyph bars in a deep green field.</summary>
internal static class Example41_ExitTheMatrix
{
    [FragmentShader]
    public static void ExitTheMatrix(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution;
        var grid = uv * new float2(18f, 10f);
        var cell = new float2(grid.x - maths.floor(grid.x) - 0.5f, grid.y - maths.floor(grid.y) - 0.5f);
        var column = maths.floor(grid.x);
        var drift = constants.Time * (0.7f + 0.08f * maths.sin(column * 4.1f));
        var trail = 0.5f + 0.5f * maths.sin((grid.y - drift) * 5.2f + column * 1.8f);
        var glyph = 1f - maths.smoothStep(0.13f, 0.24f, maths.abs(cell.x + 0.08f * maths.sin(column)));
        var head = maths.exp(-maths.abs(grid.y - drift - trail * 0.2f) * 3.5f);
        var intensity = maths.clamp(glyph * (0.15f + trail * 0.35f + head * 0.8f), 0f, 1f);
        color = new float4(0.005f + intensity * 0.02f, 0.04f + intensity * 0.64f, 0.025f + intensity * 0.18f, 1f);
    }
}
