using DeltaMaths;
using DeltaShader.Abstractions;

namespace DeltaShader.TestShaders;

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

    [VertexShader("EditorViewportCubeVertex")]
    public static void Vertex(
        [VertexInput(0, Binding = 0, ByteOffset = 0)] float3 position,
        [VertexInput(1, Binding = 0, ByteOffset = 12)] float3 normal,
        [VertexInput(2, Binding = 0, ByteOffset = 24)] float2 uv,
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<SceneParameters> scene,
        [Position] out float4 clipPosition,
        [ShaderVarying(0)] out float3 worldNormal,
        [ShaderVarying(1)] out float2 texCoord)
    {
        var modelPosition = scene[0].Model * new float4(position, 1f);
        clipPosition = scene[0].Projection * scene[0].View * modelPosition;
        worldNormal = maths.normalize((scene[0].Model * new float4(normal, 0f)).xyz);
        texCoord = uv;
    }

    [FragmentShader("EditorViewportCubeFragment")]
    public static void Fragment(
        [ReadOnlyStorageBuffer(0, 0)] ReadOnlyStorageBuffer<SceneParameters> scene,
        [SampledTexture2D(0, 1)] SampledTexture2D albedo,
        [ShaderVarying(0)] float3 worldNormal,
        [ShaderVarying(1)] float2 texCoord,
        [FragmentColor] out float4 color)
    {
        var baseColor = ShaderIntrinsics.SampleFragment<float2, float4>(albedo, texCoord);
        var lightDirection = maths.normalize(-scene[0].LightDirection);
        var diffuse = maths.max(0f, maths.dot(worldNormal, lightDirection));
        color = baseColor * scene[0].LightColor * diffuse;
    }
}
