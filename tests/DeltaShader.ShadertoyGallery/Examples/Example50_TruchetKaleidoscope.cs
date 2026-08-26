using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Kaleidoscopic Truchet-like arcs built from repeated tile coordinates.</summary>
internal static class Example50_TruchetKaleidoscope
{
    [FragmentShader]
    public static void TruchetKaleidoscope(
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] GalleryConstants constants,
        [FragmentColor] out float4 color)
    {
        var uv = fragmentCoord / constants.Resolution;
        var p = uv * 6f - new float2(3f, 3f);
        p.x = maths.abs(p.x);
        p.y = maths.abs(p.y);
        var tile = new float2(maths.floor(p.x), maths.floor(p.y));
        var cell = new float2(p.x - tile.x - 0.5f, p.y - tile.y - 0.5f);
        var selector = maths.sin(maths.dot(tile, new float2(8.7f, 13.1f)));
        var corner = 0.5f * selector;
        var arcCenter = new float2(corner, corner);
        var arc = maths.abs(maths.length(cell - arcCenter) - 0.5f);
        var line = maths.exp(-arc * 70f);
        var center = maths.exp(-maths.dot(cell, cell) * 8f);
        var pulse = 0.7f + 0.3f * maths.sin(constants.Time + tile.x * 0.7f + tile.y * 1.1f);
        color = new float4(0.025f + line * 0.13f + center * 0.08f, 0.04f + line * 0.27f, 0.12f + line * 0.58f + center * 0.18f, 1f) * pulse;
    }
}
