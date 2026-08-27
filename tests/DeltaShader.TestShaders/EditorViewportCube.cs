using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.TestShaders;

internal static class EditorViewportCube
{
    public struct CubeVertex
    {
        public float3 Position = default;
        public float3 Normal = default;
        public float2 Uv = default;

        public CubeVertex()
        {
        }
    }

    public struct SceneParameters
    {
        public float4x4 Model = default;
        public float4x4 View = default;
        public float4x4 Projection = default;
        public float3 LightDirection = default;
        public float _Padding0 = default;
        public float4 LightColor = default;

        public SceneParameters()
        {
        }
    }

    [Varying]
    public struct CubeVarying
    {
        [Position]
        [Layout(0)]
        public float4 Position;
        [Layout(1)]
        public float3 Normal;
        [Layout(2)]
        public float2 Uv;
    }

    public readonly struct VertexContext
    {
        [Varying]
        public readonly CubeVarying Vertex;

        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<SceneParameters> Scene;
    }

    public readonly struct FragmentContext
    {
        [Varying]
        public readonly CubeVarying Fragment;

        [Layout(0, 0)]
        public readonly ReadOnlyStorageBuffer<SceneParameters> Scene;

        [Layout(0, 1)]
        public readonly SampledTexture2D Albedo;
    }

    [VertexShader("EditorViewportCubeVertex")]
    public static CubeVarying Vertex(in VertexContext context)
    {
        var modelPosition = context.Scene[0].Model * context.Vertex.Position;
        return new CubeVarying
        {
            Position = context.Scene[0].Projection * context.Scene[0].View * modelPosition,
            Normal = maths.normalize((context.Scene[0].Model * new float4(context.Vertex.Normal, 0f)).xyz),
            Uv = context.Vertex.Uv
        };
    }

    [FragmentShader("EditorViewportCubeFragment")]
    public static float4 Fragment(in FragmentContext context)
    {
        var baseColor = ShaderIntrinsics.SampleFragment<float2, float4>(context.Albedo, context.Fragment.Uv);
        var lightDirection = maths.normalize(-context.Scene[0].LightDirection);
        var diffuse = maths.max(0f, maths.dot(context.Fragment.Normal, lightDirection));
        return baseColor * context.Scene[0].LightColor * diffuse;
    }
}
