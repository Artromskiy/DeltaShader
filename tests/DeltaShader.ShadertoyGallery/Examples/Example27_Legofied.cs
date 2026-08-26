using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Quantized tiles with soft seams, echoing a colorful block mosaic.</summary>
internal static class Example27_Legofied
{
    [FragmentShader]
    public static void Legofied(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = fragmentCoord / constants.Resolution;
        var grid = uv * 9f + new float2(constants.Time * 0.08f, -constants.Time * 0.05f);
        var cell = new float2(grid.x - maths.floor(grid.x) - 0.5f, grid.y - maths.floor(grid.y) - 0.5f);
        var seam = 1f - maths.smoothStep(0.38f, 0.49f, maths.max(maths.abs(cell.x), maths.abs(cell.y)));
        var tile = new float2(maths.floor(grid.x), maths.floor(grid.y));
        var phase = maths.sin(maths.dot(tile, new float2(12.7f, 28.3f)));
        var red = 0.5f + 0.5f * maths.sin(phase * 5f + 0.8f);
        var green = 0.5f + 0.5f * maths.sin(phase * 7f + 2.2f);
        var blue = 0.5f + 0.5f * maths.sin(phase * 9f + 4.1f);
        var bevel = 0.7f + 0.3f * (1f - maths.length(cell) * 1.5f);
        color = new float4(red, green, blue, 1f) * seam * bevel + new float4(0.015f, 0.02f, 0.035f, 1f) * (1f - seam);
    }
}
