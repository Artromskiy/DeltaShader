using Delta.Shader.Compiler;
using Delta.Shader.Contract;

namespace Delta.Shader.Tool;

/// <summary>
/// Build-time identity and final artifact for one prepared UI shader variant.
/// </summary>
public sealed class UiShaderVariantArtifact
{
    internal UiShaderVariantArtifact(
        UiShaderVariantKey key,
        string variantIdentity,
        GraphicsShaderProgram program)
    {
        Key = key;
        VariantIdentity = variantIdentity;
        Program = program;
    }

    public UiShaderVariantKey Key { get; }
    public string VariantIdentity { get; }
    public GraphicsShaderProgram Program { get; }
}
