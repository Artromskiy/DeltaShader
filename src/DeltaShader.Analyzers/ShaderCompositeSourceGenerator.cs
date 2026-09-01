using System;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Analyzers;

/// <summary>
/// Emits a generated program for an editor-selected shader composite.
/// </summary>
public static class ShaderCompositeSourceGenerator
{
    public static bool TryGenerate(
        string className,
        IMethodSymbol vertexMethod,
        IMethodSymbol fragmentMethod,
        ShaderCompositeCompilationResult composition,
        out string source,
        out string? reason)
    {
        source = string.Empty;
        reason = null;
        if (vertexMethod is null)
        {
            throw new ArgumentNullException(nameof(vertexMethod));
        }

        if (fragmentMethod is null)
        {
            throw new ArgumentNullException(nameof(fragmentMethod));
        }

        if (composition is null)
        {
            throw new ArgumentNullException(nameof(composition));
        }
        if (string.IsNullOrWhiteSpace(className))
        {
            reason = "A composite generated class requires a non-empty name.";
            return false;
        }

        if (!composition.Success || composition.Vertex is null || composition.Fragment is null)
        {
            reason = "The composite must compile successfully before source generation.";
            return false;
        }

        var vertexManifest = composition.GetBuildManifest(ShaderStage.Vertex);
        var fragmentManifest = composition.GetBuildManifest(ShaderStage.Fragment);
        if (!ArtifactSourceEmitter.TryEmitPackingMethods(
                vertexMethod,
                vertexManifest,
                out var vertexPacking,
                out reason,
                className + "Vertex") ||
            !ArtifactSourceEmitter.TryEmitPackingMethods(
                fragmentMethod,
                fragmentManifest,
                out var fragmentPacking,
                out reason,
                className + "Fragment"))
        {
            return false;
        }

        source = GeneratedArtifactSource.Graphics(
            vertexMethod,
            className,
            ArtifactSourceEmitter.EmitAbiFactory(vertexManifest),
            ArtifactSourceEmitter.EmitAbiFactory(fragmentManifest, "CreateFragmentAbi"),
            ArtifactSourceEmitter.EmitAbiAccessor("VertexAbi", "CreateAbi"),
            ArtifactSourceEmitter.EmitAbiAccessor("FragmentAbi", "CreateFragmentAbi"),
            vertexPacking,
            fragmentPacking,
            "composite.vert.spv",
            "composite.frag.spv",
            GeneratedArtifactSource.GraphicsAbiProjection(vertexMethod, className, string.Empty),
            GeneratedArtifactSource.GraphicsFacadeProjection(vertexMethod, className, string.Empty));
        return true;
    }
}
