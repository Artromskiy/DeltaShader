using Delta.Maths;
using Delta.Shader;

namespace Delta.Shader.Mesh;

[Interstage]
public struct MeshPayload
{
    [Layout(0)]
    public Position Position;

    [Layout(1)]
    public WorldNormal Normal;

    [Layout(2)]
    public Uv0 Uv;
}

public readonly struct MeshVertexContext
{
    [Interstage]
    public readonly MeshPayload Vertex;

    public MeshVertexContext(MeshPayload vertex)
    {
        Vertex = vertex;
    }
}

public readonly struct MeshFragmentContext
{
    [Interstage]
    public readonly MeshPayload Fragment;

    public MeshFragmentContext(MeshPayload fragment)
    {
        Fragment = fragment;
    }
}

public static class MeshShaders
{
    [VertexShader("mesh")]
    public static MeshPayload Mesh(in MeshVertexContext context) => context.Vertex;

    [FragmentShader("mesh")]
    public static float4 Fragment(in MeshFragmentContext context) =>
        new float4(context.Fragment.Uv.Value, 0f, 1f);
}
