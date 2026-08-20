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

    [FragmentShader("SdfTextFragment")]
    public static void SdfText(
        [SampledTexture2D(0, 3)] SampledTexture2D atlas,
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var p = fragmentCoord * 0.01f - new float2(0.5f, 0.5f);
        var q = maths.abs(p) - new float2(0.35f, 0.22f) + 0.08f;
        var distance = maths.length(maths.max(q, new float2(0f, 0f))) +
            maths.min(maths.max(q.x, q.y), 0f) - 0.08f;
        var edge = ShaderIntrinsics.fwidth(distance);
        var coverage = 1f - maths.smoothStep(-edge, edge, distance);
        color = parameters.TextColor * coverage;
    }

    [FragmentShader("MsdfTextFragment")]
    public static void MsdfText(
        [SampledTexture2D(0, 4)] SampledTexture2D atlas,
        [FragmentCoord] float2 fragmentCoord,
        [PushConstant] TextParameters parameters,
        [FragmentColor] out float4 color)
    {
        var texel = ShaderIntrinsics.SampleFragment<float2, float4>(atlas, fragmentCoord);
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
