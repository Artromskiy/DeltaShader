using Delta.Maths;
using Delta.Shader.Abstractions;

namespace Delta.Shader.TestShaders;

public static class GeneratedTextShaders
{
    public struct TextParameters
    {
        public float4 TextColor;
        public float4 OutlineColor;
        public float OutlineWidth;
    }

    [VertexShader("sdf-text")]
    public static void SdfTextVertex(
        [VertexIndex] uint vertexIndex,
        [Position] out float4 position,
        [ShaderVarying(0)] out float2 uv)
    {
        var x = (vertexIndex == 0u || vertexIndex == 2u) ? -1f : 3f;
        var y = vertexIndex == 2u ? 3f : -1f;
        position = new float4(x, y, 0f, 1f);
        uv = new float2((x + 1f) * 0.25f, (y + 1f) * 0.25f);
    }

    [FragmentShader("sdf-text")]
    public static void SdfTextFragment(
        [SampledTexture2D(0, 3)] SampledTexture2D atlas,
        [ShaderVarying(0)] float2 uv,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var p = uv - new float2(0.5f, 0.5f);
        var q = maths.abs(p) - new float2(0.35f, 0.22f) + 0.08f;
        var distance = maths.length(maths.max(q, new float2(0f, 0f))) +
            maths.min(maths.max(q.x, q.y), 0f) - 0.08f;
        var edge = ShaderIntrinsics.fwidth(distance);
        var coverage = 1f - maths.smoothStep(-edge, edge, distance);
        color = parameters.TextColor * coverage;
    }

    [VertexShader("msdf-text")]
    public static void MsdfTextVertex(
        [VertexIndex] uint vertexIndex,
        [Position] out float4 position,
        [ShaderVarying(0)] out float2 uv)
    {
        var x = (vertexIndex == 0u || vertexIndex == 2u) ? -1f : 3f;
        var y = vertexIndex == 2u ? 3f : -1f;
        position = new float4(x, y, 0f, 1f);
        uv = new float2((x + 1f) * 0.25f, (y + 1f) * 0.25f);
    }

    [FragmentShader("msdf-text")]
    public static void MsdfTextFragment(
        [SampledTexture2D(0, 4)] SampledTexture2D atlas,
        [ShaderVarying(0)] float2 uv,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, uv);
        var median = maths.max(
            maths.min(texel.x, texel.y),
            maths.min(maths.max(texel.x, texel.y), texel.z));
        var signedDistance = median - 0.5f;
        var edge = ShaderIntrinsics.fwidth(signedDistance);
        var coverage = 1f - maths.smoothStep(-edge, edge, signedDistance);
        var outline = 1f - coverage;
        color = parameters.TextColor * coverage + parameters.OutlineColor * outline * parameters.OutlineWidth;
    }
}
