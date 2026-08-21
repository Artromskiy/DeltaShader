using Delta.Maths;
using Delta.Shader.Abstractions;

namespace Delta.Shader.TestShaders;

public static class EditorViewportCube
{
    public struct CubeVertex
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;
    }

    public struct SceneParameters
    {
        public float4x4 Model;
        public float4x4 View;
        public float4x4 Projection;
        public float3 LightDirection;
        public float _Padding0;
        public float4 LightColor;
    }

    [VertexShader("EditorViewportCubeVertex")]
    public static void Vertex(
        [VertexInput(0)] float3 position,
        [VertexInput(1)] float3 normal,
        [VertexInput(2)] float2 uv,
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
