using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Text;

public struct GlyphInstance
{
    public float2 PixelMin = default;
    public float2 PixelMax = default;
    public float4 UvRect = default;
    public float4 Color = default;

    public GlyphInstance()
    {
    }
}

public struct TextParameters
{
    public float2 Resolution = default;
    public float4 TextColor = default;
    public float4 OutlineColor = default;
    public float OutlineWidth = default;

    public TextParameters()
    {
    }
}

[Interstage]
public struct TextVarying
{
    [Position]
    public float4 Position;
    public float2 Uv;
    public float4 GlyphColor;
}

public readonly struct TextVertexContext
{
    [Interstage]
    public readonly TextVarying Vertex;

    [Layout(0, 0)]
    public readonly ReadOnlyStorageBuffer<GlyphInstance> Glyphs;

    [PushConstant]
    public readonly TextParameters Parameters;
}

public readonly struct SdfTextFragmentContext
{
    [Interstage]
    public readonly TextVarying Fragment;

    [Layout(0, 3)]
    public readonly SampledTexture2D Atlas;

    [PushConstant]
    public readonly TextParameters Parameters;
}

public readonly struct MsdfTextFragmentContext
{
    [Interstage]
    public readonly TextVarying Fragment;

    [Layout(0, 4)]
    public readonly SampledTexture2D Atlas;

    [PushConstant]
    public readonly TextParameters Parameters;
}

public static class TextShaders
{
    [VertexShader("sdf-text")]
    [SuppressMessage("Design", "CA1062", Justification = "Shader entry points are compile-time authoring methods; the analyzer lowers context resource fields instead of executing them on the CLR.")]
    public static TextVarying SdfTextVertex(in TextVertexContext context)
    {
        uint instanceIndex = ShaderBuiltins.InstanceIndex;
        uint vertexIndex = ShaderBuiltins.VertexIndex;
        var glyph = context.Glyphs[instanceIndex];
        var min = glyph.PixelMin;
        var max = glyph.PixelMax;
        var uvMin = new float2(glyph.UvRect.x, glyph.UvRect.y);
        var uvMax = new float2(glyph.UvRect.z, glyph.UvRect.w);

        if (vertexIndex == 0u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = uvMin,
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 1u)
        {
            return new TextVarying
            {
                Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMax.x, uvMin.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 2u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMin.x, uvMax.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 3u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMin.x, uvMax.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 4u)
        {
            return new TextVarying
            {
                Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMax.x, uvMin.y),
                GlyphColor = glyph.Color
            };
        }

        return new TextVarying
        {
            Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
            Uv = uvMax,
            GlyphColor = glyph.Color
        };
    }

    [FragmentShader("sdf-text")]
    public static float4 SdfTextFragment(in SdfTextFragmentContext context)
    {
        var texel = context.Atlas.Sample<float2, float4>(context.Fragment.Uv);
        var distance = texel.x - 0.5f;
        var edge = ShaderIntrinsics.fwidth(distance);
        var coverage = maths.smoothstep(-edge, edge, distance);
        return context.Parameters.TextColor * context.Fragment.GlyphColor * coverage;
    }

    [VertexShader("msdf-text")]
    [SuppressMessage("Design", "CA1062", Justification = "Shader entry points are compile-time authoring methods; the analyzer lowers context resource fields instead of executing them on the CLR.")]
    public static TextVarying MsdfTextVertex(in TextVertexContext context)
    {
        uint instanceIndex = ShaderBuiltins.InstanceIndex;
        uint vertexIndex = ShaderBuiltins.VertexIndex;
        var glyph = context.Glyphs[instanceIndex];
        var min = glyph.PixelMin;
        var max = glyph.PixelMax;
        var uvMin = new float2(glyph.UvRect.x, glyph.UvRect.y);
        var uvMax = new float2(glyph.UvRect.z, glyph.UvRect.w);

        if (vertexIndex == 0u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = uvMin,
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 1u)
        {
            return new TextVarying
            {
                Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMax.x, uvMin.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 2u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMin.x, uvMax.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 3u)
        {
            return new TextVarying
            {
                Position = new float4((min.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMin.x, uvMax.y),
                GlyphColor = glyph.Color
            };
        }
        else if (vertexIndex == 4u)
        {
            return new TextVarying
            {
                Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (min.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
                Uv = new float2(uvMax.x, uvMin.y),
                GlyphColor = glyph.Color
            };
        }

        return new TextVarying
        {
            Position = new float4((max.x / context.Parameters.Resolution.x) * 2f - 1f, 1f - (max.y / context.Parameters.Resolution.y) * 2f, 0f, 1f),
            Uv = uvMax,
            GlyphColor = glyph.Color
        };
    }

    [FragmentShader("msdf-text")]
    public static float4 MsdfTextFragment(in MsdfTextFragmentContext context)
    {
        var texel = context.Atlas.Sample<float2, float4>(context.Fragment.Uv);
        var median = maths.max(
            maths.min(texel.x, texel.y),
            maths.min(maths.max(texel.x, texel.y), texel.z));
        var signedDistance = median - 0.5f;
        var edge = ShaderIntrinsics.fwidth(signedDistance);
        var fillCoverage = maths.smoothstep(-edge, edge, signedDistance);
        var outerCoverage = maths.smoothstep(-edge, edge, signedDistance + context.Parameters.OutlineWidth);
        var outlineContribution = maths.max(outerCoverage - fillCoverage, 0f);
        return context.Parameters.TextColor * context.Fragment.GlyphColor * fillCoverage +
            context.Parameters.OutlineColor * context.Fragment.GlyphColor * outlineContribution;
    }
}
