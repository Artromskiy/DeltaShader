using Delta.Maths;
using Delta.Shader.Abstractions;

namespace Delta.Shader.Compiler.ReferenceFixtures;

public struct TransformConstants
{
    public float4x4 Model;
    public float4x4 View;
    public float4x4 Projection;
}

public static class TransformVertex
{
    [VertexShader("CubeVertex")]
    public static void Vertex(
        [PushConstant] TransformConstants constants,
        [Position] out float4 position)
    {
        var vertex = new float3(1f, 2f, 3f);
        position = constants.Projection * constants.View * constants.Model * new float4(vertex, 1f);
    }
}
