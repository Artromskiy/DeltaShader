using System;
using Compiler = Delta.Shader.Compiler;
using Final = Delta.Shader.Contract;

namespace Delta.Shader.Tool;

/// <summary>
/// Materializes selected composite compiler output through the existing final artifact contract.
/// </summary>
public static class ShaderCompositeArtifactPublisher
{
    public static Final.GraphicsShaderProgram Create(
        Compiler.ShaderCompositeCompilationResult composition,
        ReadOnlySpan<byte> vertexSpirv,
        ReadOnlySpan<byte> fragmentSpirv)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (!composition.Success || composition.Vertex is null || composition.Fragment is null)
        {
            throw new ArgumentException("The composite must compile successfully before artifact publication.", nameof(composition));
        }

        var vertexManifest = composition.GetBuildManifest(Delta.Shader.ShaderStage.Vertex);
        var fragmentManifest = composition.GetBuildManifest(Delta.Shader.ShaderStage.Fragment);
        var vertex = ShaderArtifactPublisher.Create(vertexSpirv, vertexManifest);
        var fragment = ShaderArtifactPublisher.Create(fragmentSpirv, fragmentManifest);
        return new Final.GraphicsShaderProgram(vertex, fragment);
    }
}
