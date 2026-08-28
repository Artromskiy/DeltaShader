using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Playground;

public static class JfaShaders
{
    [Interstage]
    public struct JfaVarying
    {
        [Position]
        public float4 Position;

        public float2 Uv;
    }

    public readonly struct JfaInitVertexContext
    {
        [Interstage]
        public readonly JfaVarying Vertex;
    }

    public readonly struct JfaInitFragmentContext
    {
        [Interstage]
        public readonly JfaVarying Fragment;

        [Layout(0, 0)]
        public readonly SampledTexture2D Silhouette;
    }

    public readonly struct JfaFloodParameters
    {
        public readonly float2 TexelSize;
        public readonly float Jump;
    }

    public readonly struct JfaFloodVertexContext
    {
        [Interstage]
        public readonly JfaVarying Vertex;
    }

    public readonly struct JfaFloodFragmentContext
    {
        [Interstage]
        public readonly JfaVarying Fragment;

        [Layout(0, 0)]
        public readonly SampledTexture2D Seeds;

        [PushConstant]
        public readonly JfaFloodParameters Parameters;
    }

    public readonly struct JfaCompositeParameters
    {
        public readonly float2 TexelSize;
        public readonly float OutlineWidth;
        public readonly float4 Color;
    }

    public readonly struct JfaCompositeVertexContext
    {
        [Interstage]
        public readonly JfaVarying Vertex;
    }

    public readonly struct JfaCompositeFragmentContext
    {
        [Interstage]
        public readonly JfaVarying Fragment;

        [Layout(0, 0)]
        public readonly SampledTexture2D Seeds;

        [Layout(0, 1)]
        public readonly SampledTexture2D Silhouette;

        [PushConstant]
        public readonly JfaCompositeParameters Parameters;
    }

    [VertexShader("jfa-init")]
    public static JfaVarying JfaInitVertex(in JfaInitVertexContext context)
    {
        uint vertex = ShaderBuiltins.VertexIndex;
        if (vertex == 0u)
        {
            return new JfaVarying
            {
                Position = new float4(-1f, -1f, 0f, 1f),
                Uv = new float2(0f, 0f)
            };
        }

        if (vertex == 1u)
        {
            return new JfaVarying
            {
                Position = new float4(3f, -1f, 0f, 1f),
                Uv = new float2(2f, 0f)
            };
        }

        return new JfaVarying
        {
            Position = new float4(-1f, 3f, 0f, 1f),
            Uv = new float2(0f, 2f)
        };
    }

    [FragmentShader("jfa-init")]
    public static float4 JfaInitFragment(in JfaInitFragmentContext context)
    {
        float2 uv = context.Fragment.Uv;
        float4 silhouette = context.Silhouette.Sample<float2, float4>(uv);
        float valid = silhouette.a > 0.001f ? 1f : 0f;
        return new float4(uv.x, uv.y, valid, 1f);
    }

    [VertexShader("jfa-flood")]
    public static JfaVarying JfaFloodVertex(in JfaFloodVertexContext context)
    {
        uint vertex = ShaderBuiltins.VertexIndex;
        if (vertex == 0u)
        {
            return new JfaVarying
            {
                Position = new float4(-1f, -1f, 0f, 1f),
                Uv = new float2(0f, 0f)
            };
        }

        if (vertex == 1u)
        {
            return new JfaVarying
            {
                Position = new float4(3f, -1f, 0f, 1f),
                Uv = new float2(2f, 0f)
            };
        }

        return new JfaVarying
        {
            Position = new float4(-1f, 3f, 0f, 1f),
            Uv = new float2(0f, 2f)
        };
    }

    [FragmentShader("jfa-flood")]
    public static float4 JfaFloodFragment(in JfaFloodFragmentContext context)
    {
        float2 uv = context.Fragment.Uv;
        float2 offset = context.Parameters.TexelSize * context.Parameters.Jump;
        float4 center = context.Seeds.Sample<float2, float4>(ClampUv(uv));
        float2 best = center.z > 0.5f ? center.xy : new float2(-1f, -1f);

        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(0f, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, -offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, 0f))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, 0f))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(-offset.x, offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(0f, offset.y))));
        best = ChooseNearest(uv, best, context.Seeds.Sample<float2, float4>(ClampUv(uv + new float2(offset.x, offset.y))));

        float valid = best.x >= 0f ? 1f : 0f;
        return new float4(best.x, best.y, valid, 1f);
    }

    [VertexShader("jfa-composite")]
    public static JfaVarying JfaCompositeVertex(in JfaCompositeVertexContext context)
    {
        uint vertex = ShaderBuiltins.VertexIndex;
        if (vertex == 0u)
        {
            return new JfaVarying
            {
                Position = new float4(-1f, -1f, 0f, 1f),
                Uv = new float2(0f, 0f)
            };
        }

        if (vertex == 1u)
        {
            return new JfaVarying
            {
                Position = new float4(3f, -1f, 0f, 1f),
                Uv = new float2(2f, 0f)
            };
        }

        return new JfaVarying
        {
            Position = new float4(-1f, 3f, 0f, 1f),
            Uv = new float2(0f, 2f)
        };
    }

    [FragmentShader("jfa-composite")]
    public static float4 JfaCompositeFragment(in JfaCompositeFragmentContext context)
    {
        float2 uv = context.Fragment.Uv;
        float4 silhouette = context.Silhouette.Sample<float2, float4>(ClampUv(uv));
        if (silhouette.a > 0.001f || context.Parameters.OutlineWidth <= 0f)
        {
            return new float4(0f, 0f, 0f, 0f);
        }

        float4 seed = context.Seeds.Sample<float2, float4>(ClampUv(uv));
        if (seed.z <= 0.5f)
        {
            return new float4(0f, 0f, 0f, 0f);
        }

        float texel = maths.max(context.Parameters.TexelSize.x, context.Parameters.TexelSize.y);
        float distanceInPixels = maths.distance(uv, seed.xy) / texel;
        float aa = ShaderIntrinsics.fwidth(distanceInPixels);
        float coverage = 1f - maths.smoothstep(
            context.Parameters.OutlineWidth - aa,
            context.Parameters.OutlineWidth + aa,
            distanceInPixels);

        return context.Parameters.Color * coverage;
    }

    private static float2 ChooseNearest(float2 pixel, float2 best, float4 candidate)
    {
        if (candidate.z <= 0.5f)
        {
            return best;
        }

        if (best.x < 0f || maths.distance(pixel, candidate.xy) < maths.distance(pixel, best))
        {
            return candidate.xy;
        }

        return best;
    }

    private static float2 ClampUv(float2 uv)
    {
        return maths.clamp(uv, new float2(0f, 0f), new float2(1f, 1f));
    }
}
