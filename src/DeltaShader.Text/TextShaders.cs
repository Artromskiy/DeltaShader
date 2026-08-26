using DeltaMaths;
using DeltaShader.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace DeltaShader.Text;

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible std430 payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
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

[SuppressMessage("Design", "CA1051", Justification = "Public fields are the declared shader-visible push-constant payload.")]
[SuppressMessage("Design", "CA1815", Justification = "Field-only shader ABI record; equality is not part of the serialized layout contract.")]
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

public static class TextShaders
{
    [VertexShader("sdf-text")]
    [SuppressMessage("Design", "CA1062", Justification = "Shader entry points are compile-time authoring methods; the analyzer lowers resource parameters instead of executing them on the CLR.")]
    public static void SdfTextVertex(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<GlyphInstance> glyphs,
        [InstanceIndex] uint instanceIndex,
        [VertexIndex] uint vertexIndex,
        [Position] out float4 position,
        [ShaderVarying(0)] out float2 uv,
        [ShaderVarying(1)] out float4 glyphColor,
        [PushConstant] TextParameters parameters)
    {
        var glyph = glyphs[instanceIndex];
        var min = glyph.PixelMin;
        var max = glyph.PixelMax;
        var uvMin = new float2(glyph.UvRect.x, glyph.UvRect.y);
        var uvMax = new float2(glyph.UvRect.z, glyph.UvRect.w);

        if (vertexIndex == 0u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = uvMin;
        }
        else if (vertexIndex == 1u)
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMax.x, uvMin.y);
        }
        else if (vertexIndex == 2u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMin.x, uvMax.y);
        }
        else if (vertexIndex == 3u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMin.x, uvMax.y);
        }
        else if (vertexIndex == 4u)
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMax.x, uvMin.y);
        }
        else
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = uvMax;
        }

        glyphColor = glyph.Color;
    }

    [FragmentShader("sdf-text")]
    public static void SdfTextFragment(
        [SampledTexture2D(0, 3)] SampledTexture2D atlas,
        [ShaderVarying(0)] float2 uv,
        [ShaderVarying(1)] float4 glyphColor,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, uv);
        var distance = texel.x - 0.5f;
        var edge = ShaderIntrinsics.fwidth(distance);
        var coverage = maths.smoothStep(-edge, edge, distance);
        color = parameters.TextColor * glyphColor * coverage;
    }

    [VertexShader("msdf-text")]
    [SuppressMessage("Design", "CA1062", Justification = "Shader entry points are compile-time authoring methods; the analyzer lowers resource parameters instead of executing them on the CLR.")]
    public static void MsdfTextVertex(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<GlyphInstance> glyphs,
        [InstanceIndex] uint instanceIndex,
        [VertexIndex] uint vertexIndex,
        [Position] out float4 position,
        [ShaderVarying(0)] out float2 uv,
        [ShaderVarying(1)] out float4 glyphColor,
        [PushConstant] TextParameters parameters)
    {
        var glyph = glyphs[instanceIndex];
        var min = glyph.PixelMin;
        var max = glyph.PixelMax;
        var uvMin = new float2(glyph.UvRect.x, glyph.UvRect.y);
        var uvMax = new float2(glyph.UvRect.z, glyph.UvRect.w);

        if (vertexIndex == 0u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = uvMin;
        }
        else if (vertexIndex == 1u)
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMax.x, uvMin.y);
        }
        else if (vertexIndex == 2u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMin.x, uvMax.y);
        }
        else if (vertexIndex == 3u)
        {
            position = new float4((min.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMin.x, uvMax.y);
        }
        else if (vertexIndex == 4u)
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (min.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = new float2(uvMax.x, uvMin.y);
        }
        else
        {
            position = new float4((max.x / parameters.Resolution.x) * 2f - 1f, 1f - (max.y / parameters.Resolution.y) * 2f, 0f, 1f);
            uv = uvMax;
        }

        glyphColor = glyph.Color;
    }

    [FragmentShader("msdf-text")]
    public static void MsdfTextFragment(
        [SampledTexture2D(0, 4)] SampledTexture2D atlas,
        [ShaderVarying(0)] float2 uv,
        [ShaderVarying(1)] float4 glyphColor,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, uv);
        var median = maths.max(
            maths.min(texel.x, texel.y),
            maths.min(maths.max(texel.x, texel.y), texel.z));
        var signedDistance = median - 0.5f;
        var edge = ShaderIntrinsics.fwidth(signedDistance);
        var fillCoverage = maths.smoothStep(-edge, edge, signedDistance);
        var outerCoverage = maths.smoothStep(-edge, edge, signedDistance + parameters.OutlineWidth);
        var outlineContribution = maths.max(outerCoverage - fillCoverage, 0f);
        color = parameters.TextColor * glyphColor * fillCoverage + parameters.OutlineColor * glyphColor * outlineContribution;
    }
}
