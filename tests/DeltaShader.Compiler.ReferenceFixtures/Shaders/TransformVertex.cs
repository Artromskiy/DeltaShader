using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Compiler.ReferenceFixtures;

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
    [Interstage]
    public struct VertexOutput
    {
        public Position Position;
    }

    public readonly struct VertexContext
    {
        public VertexContext(VertexOutput vertex, TransformConstants constants)
        {
            Vertex = vertex;
            Constants = constants;
        }

        [Interstage]
        public readonly VertexOutput Vertex;

        [PushConstant]
        public readonly TransformConstants Constants;
    }

    [VertexShader("CubeVertex")]
    public static VertexOutput Vertex(in VertexContext context)
    {
        var vertex = new float3(1f, 2f, 3f);
        return new VertexOutput
        {
            Position = context.Constants.Projection * context.Constants.View * context.Constants.Model * new float4(vertex, 1f)
        };
    }
}
