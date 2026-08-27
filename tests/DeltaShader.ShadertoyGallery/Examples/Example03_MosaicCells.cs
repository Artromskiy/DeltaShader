using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Repeated cells whose window brightness drifts over time.</summary>
internal static class Example03_MosaicCells
{
    [FragmentShader]
    public static void MosaicCells(
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / constants.Resolution) * 2f - new float2(1f, 1f);
        var tile = p * 5f;
        var cell = new float2(tile.x - maths.floor(tile.x) - 0.5f, tile.y - maths.floor(tile.y) - 0.5f);
        var edge = maths.max(maths.abs(cell.x), maths.abs(cell.y));
        var window = 1f - maths.smoothStep(0.28f, 0.48f, edge);
        var pulse = 0.5f + 0.5f * maths.sin(constants.Time + maths.floor(tile.x) * 1.7f + maths.floor(tile.y) * 0.9f);
        color = new float4(0.03f + 0.45f * window * pulse, 0.06f + 0.3f * window, 0.14f + 0.65f * window * (1f - pulse), 1f);
    }
}
