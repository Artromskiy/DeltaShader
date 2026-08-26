using DeltaMaths;
using DeltaShader.Abstractions;

namespace DeltaShader.Compiler.ReferenceFixtures;

internal struct TransformConstants
{
    public float4x4 Model = default;
    public float4x4 View = default;
    public float4x4 Projection = default;

    public TransformConstants()
    {
    }
}

internal static class TransformVertex
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
