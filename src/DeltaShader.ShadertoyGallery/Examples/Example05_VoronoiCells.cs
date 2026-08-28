using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>Bounded nine-neighbor cell-distance coloring.</summary>
internal static class Example05_VoronoiCells
{
    [FragmentShader]
    public static float4 VoronoiCells(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        var grid = p * 4f;
        var cell = new float2(grid.x - maths.floor(grid.x), grid.y - maths.floor(grid.y));
        var nearest = 1.5f;
        var ring = 0f;
        for (var x = -1f; x <= 1f; x += 1f)
        {
            for (var y = -1f; y <= 1f; y += 1f)
            {
                var id = new float2(maths.floor(grid.x) + x, maths.floor(grid.y) + y);
                var seedValue = maths.sin(maths.dot(id, new float2(17.1f, 41.7f))) * 43758.5f;
                var seed = seedValue - maths.floor(seedValue);
                var point = new float2(x + 0.5f + 0.35f * maths.sin(seed * 6.28f), y + 0.5f + 0.35f * maths.cos(seed * 6.28f));
                var distance = maths.length(cell - point);
                nearest = maths.min(nearest, distance);
                ring += maths.exp(-distance * distance * 28f);
            }
        }
        return new float4(0.03f + 0.5f * ring, 0.1f + 0.65f * nearest, 0.35f + 0.5f * (1f - nearest), 1f);
    }
}
