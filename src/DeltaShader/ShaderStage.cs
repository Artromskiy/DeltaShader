namespace Delta.Shader;

/// <summary>
/// Compiler-authoring stage identity used by source attributes and typed IR.
/// It is not a runtime artifact ABI type; final artifacts expose
/// <c>Delta.Shader.Contract.ShaderStage</c>.
/// </summary>
public enum ShaderStage
{
    Compute,
    Vertex,
    Fragment,
}
