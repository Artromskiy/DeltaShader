using Delta.Graphics.Semantics;

namespace Delta.Shader.Playground;

internal static class JfaShaders
{
    private static JfaVarying FullscreenTriangleVertex(uint vertexIndex)
    {
        var local = FullscreenTriangleGeometry.GetLocal(vertexIndex);
        return new JfaVarying
        {
            Position = FullscreenTriangleGeometry.GetPosition(local),
            Uv = FullscreenTriangleGeometry.GetUv(local)
        };
    }

    [Interstage]
    public struct JfaVarying
    {
        public Position Position;

        public Uv0 Uv;
    }

    public readonly struct JfaInitVertexContext
    {
        public JfaInitVertexContext()
        {
        }
    }

    public readonly struct JfaInitFragmentContext
    {
        public JfaInitFragmentContext(SampledTexture2D silhouette)
        {
            Silhouette = silhouette;
        }
        [Layout(0, 0)]
        public readonly SampledTexture2D Silhouette;
    }

    public readonly struct JfaFloodParameters
    {
        public JfaFloodParameters(float2 texelSize, float jump)
        {
            TexelSize = texelSize;
            Jump = jump;
        }

        public readonly float2 TexelSize;
        public readonly float Jump;
    }

    public readonly struct JfaFloodVertexContext
    {
        public JfaFloodVertexContext()
        {
        }
    }

    public readonly struct JfaFloodFragmentContext
    {
        public JfaFloodFragmentContext(
            SampledTexture2D seeds,
            JfaFloodParameters parameters)
        {
            Seeds = seeds;
            Parameters = parameters;
        }
        [Layout(0, 0)]
        public readonly SampledTexture2D Seeds;

        [PushConstant]
        public readonly JfaFloodParameters Parameters;
    }

    public readonly struct JfaCompositeParameters
    {
        public JfaCompositeParameters(float2 texelSize, float outlineWidth, float4 color)
        {
            TexelSize = texelSize;
            OutlineWidth = outlineWidth;
            Color = color;
        }

        public readonly float2 TexelSize;
        public readonly float OutlineWidth;
        public readonly float4 Color;
    }

    public readonly struct JfaCompositeVertexContext
    {
        public JfaCompositeVertexContext()
        {
        }
    }

    public readonly struct JfaCompositeFragmentContext
    {
        public JfaCompositeFragmentContext(
            SampledTexture2D seeds,
            SampledTexture2D silhouette,
            JfaCompositeParameters parameters)
        {
            Seeds = seeds;
            Silhouette = silhouette;
            Parameters = parameters;
        }
        [Layout(0, 0)]
        public readonly SampledTexture2D Seeds;

        [Layout(0, 1)]
        public readonly SampledTexture2D Silhouette;

        [PushConstant]
        public readonly JfaCompositeParameters Parameters;
    }

    [VertexShader("jfa-init")]
    public static JfaVarying JfaInitVertex(in JfaInitVertexContext context, in JfaVarying input)
        => FullscreenTriangleVertex(ShaderBuiltins.VertexIndex);

    [FragmentShader("jfa-init")]
    public static float4 JfaInitFragment(in JfaInitFragmentContext context, in JfaVarying input)
    {
        float2 uv = input.Uv.Value;
        float4 silhouette = context.Silhouette.Sample<float2, float4>(uv);
        float valid = 1f - maths.step(silhouette.a, 0.001f);
        return new float4(uv.x, uv.y, valid, 1f);
    }

    [VertexShader("jfa-flood")]
    public static JfaVarying JfaFloodVertex(in JfaFloodVertexContext context, in JfaVarying input)
        => FullscreenTriangleVertex(ShaderBuiltins.VertexIndex);

    [FragmentShader("jfa-flood")]
    public static float4 JfaFloodFragment(in JfaFloodFragmentContext context, in JfaVarying input)
    {
        float2 uv = input.Uv.Value;
        float2 offset = context.Parameters.TexelSize * context.Parameters.Jump;
        float4 center = context.Seeds.Sample<float2, float4>(ClampUv(uv));
        float centerValid = 1f - maths.step(center.z, 0.5f);
        float2 best = new float2(-1f) + centerValid * (center.xy + new float2(1f));

        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(0f, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, 0f))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, 0f))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(0f, offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, offset.y))));

        float valid = maths.step(0f, best.x);
        return new float4(best.x, best.y, valid, 1f);
    }

    [VertexShader("jfa-composite")]
    public static JfaVarying JfaCompositeVertex(in JfaCompositeVertexContext context, in JfaVarying input)
        => FullscreenTriangleVertex(ShaderBuiltins.VertexIndex);

    [FragmentShader("jfa-composite")]
    public static float4 JfaCompositeFragment(in JfaCompositeFragmentContext context, in JfaVarying input)
    {
        float2 uv = input.Uv.Value;
        float4 silhouette = context.Silhouette.Sample<float2, float4>(ClampUv(uv));
        float4 seed = context.Seeds.Sample<float2, float4>(ClampUv(uv));
        float texel = maths.max(context.Parameters.TexelSize.x, context.Parameters.TexelSize.y);
        float distanceInPixels = maths.distance(uv, seed.xy) / texel;
        float aa = intrinsics.fwidth(distanceInPixels);
        float coverage = 1f - maths.smoothstep(
            context.Parameters.OutlineWidth - aa,
            context.Parameters.OutlineWidth + aa,
            distanceInPixels);

        float outsideSilhouette = maths.step(silhouette.a, 0.001f);
        float outlineEnabled = 1f - maths.step(context.Parameters.OutlineWidth, 0f);
        float validSeed = 1f - maths.step(seed.z, 0.5f);
        return context.Parameters.Color * (coverage * outsideSilhouette * outlineEnabled * validSeed);
    }

    private static float2 ChooseNearest(float2 pixel, float2 best, float4 candidate)
    {
        float2 candidateDelta = pixel - candidate.xy;
        float2 bestDelta = pixel - best;
        float candidateValid = 1f - maths.step(candidate.z, 0.5f);
        float bestValid = maths.step(0f, best.x);
        float candidateDistance = maths.dot(candidateDelta, candidateDelta);
        float bestDistance = maths.dot(bestDelta, bestDelta);
        float chooseCandidate = candidateValid * (1f - bestValid +
            bestValid * (1f - maths.step(bestDistance, candidateDistance)));
        return best + chooseCandidate * (candidate.xy - best);
    }

    private static float2 ClampUv(float2 uv)
    {
        return maths.clamp(uv, new float2(0f, 0f), new float2(1f, 1f));
    }
}
