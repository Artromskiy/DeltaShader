using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.ShadertoyGallery;

/// <summary>A folded tunnel with narrow gutters and a moving vanishing point.</summary>
internal static class Example23_FractalGutter
{
    [FragmentShader]
    public static float4 FractalGutter(in GalleryFragmentContext context)
    {
        var p = (new float2(ShaderBuiltins.FragmentCoord.X, ShaderBuiltins.FragmentCoord.Y) / context.Constants.Resolution) * 2f - new float2(1f, 1f);
        p.x = p.x * context.Constants.Resolution.x / context.Constants.Resolution.y;
        var q = p;
        var lines = 0f;
        for (var pass = 0f; pass < 4f; pass += 1f)
        {
            q = new float2(q.x + 0.22f * maths.sin(q.y * 4f + context.Constants.Time), q.y + 0.18f * maths.cos(q.x * 5f - context.Constants.Time));
            var gridX = maths.abs(maths.sin(q.x * (8f + pass * 2f)));
            var gridY = maths.abs(maths.sin(q.y * (10f + pass)));
            lines += maths.exp(-(gridX + gridY) * (7f + pass * 1.5f));
            q = q * 1.34f + new float2(0.11f, -0.07f);
        }
        var center = maths.exp(-maths.dot(p, p) * 3f);
        return new float4(0.06f + lines * 0.09f, 0.02f + lines * 0.22f + center * 0.15f, 0.11f + lines * 0.38f + center * 0.45f, 1f);
    }
}
