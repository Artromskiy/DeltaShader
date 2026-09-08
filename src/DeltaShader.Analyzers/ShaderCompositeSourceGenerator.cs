using System;
using System.Collections.Generic;
using System.Linq;
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
        out string? reason,
        string? variantIdentity = null,
        bool includeSidecar = true)
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
                ShaderStage.Vertex,
                out var vertexPacking,
                out reason,
                className + "Vertex") ||
            !ArtifactSourceEmitter.TryEmitPackingMethods(
                fragmentMethod,
                fragmentManifest,
                ShaderStage.Fragment,
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
            GeneratedArtifactSource.GraphicsFacadeProjection(vertexMethod, className, string.Empty, includeSidecar),
            variantIdentity ?? composition.VariantIdentity,
            includeSidecar);
        return true;
    }

    /// <summary>
    /// Generates a prepared UI composite whose consumer must provide both SPIR-V
    /// modules explicitly. This path has no runtime sidecar probing or file lookup.
    /// </summary>
    public static bool TryGenerateUiVariant(
        string className,
        IMethodSymbol vertexMethod,
        IMethodSymbol fragmentMethod,
        ShaderCompositeCompilationResult composition,
        string variantIdentity,
        out string source,
        out string? reason)
        => TryGenerate(
            className,
            vertexMethod,
            fragmentMethod,
            composition,
            out source,
            out reason,
            variantIdentity,
            includeSidecar: false);

    /// <summary>
    /// Generates the final build-time UI composite surface from every selected
    /// layer. Each layer keeps a typed packer which writes the one resolved ABI.
    /// </summary>
    public static bool TryGenerateBuildUiVariant(
        UiShaderCompositeBuildEntry entry,
        IReadOnlyList<IMethodSymbol> vertexMethods,
        IReadOnlyList<ShaderCompilationManifest> vertexManifests,
        IReadOnlyList<IMethodSymbol> fragmentMethods,
        IReadOnlyList<ShaderCompilationManifest> fragmentManifests,
        ShaderCompositeCompilationResult composition,
        string variantIdentity,
        out string source,
        out string? reason)
    {
        source = string.Empty;
        reason = null;
        if (vertexMethods.Count == 0 || fragmentMethods.Count == 0 ||
            vertexMethods.Count != vertexManifests.Count || fragmentMethods.Count != fragmentManifests.Count)
        {
            reason = "A build-time UI composite requires matching non-empty method and manifest lists.";
            return false;
        }

        if (!composition.Success || composition.Vertex is null || composition.Fragment is null)
        {
            reason = "The UI composite must compile successfully before source generation.";
            return false;
        }

        if (!TryEmitLayerPackers(entry.Name + "Vertex", vertexMethods, vertexManifests, out var vertexPacking, out reason) ||
            !TryEmitLayerPackers(entry.Name + "Fragment", fragmentMethods, fragmentManifests, out var fragmentPacking, out reason))
        {
            return false;
        }

        var vertexManifest = composition.GetBuildManifest(ShaderStage.Vertex);
        var fragmentManifest = composition.GetBuildManifest(ShaderStage.Fragment);
        source = GeneratedArtifactSource.Graphics(
            vertexMethods[0],
            entry.GeneratedProgramType,
            ArtifactSourceEmitter.EmitAbiFactory(vertexManifest),
            ArtifactSourceEmitter.EmitAbiFactory(fragmentManifest, "CreateFragmentAbi"),
            ArtifactSourceEmitter.EmitAbiAccessor("VertexAbi", "CreateAbi"),
            ArtifactSourceEmitter.EmitAbiAccessor("FragmentAbi", "CreateFragmentAbi"),
            vertexPacking,
            fragmentPacking,
            entry.VertexSpirvFileName,
            entry.FragmentSpirvFileName,
            string.Empty,
            string.Empty,
            variantIdentity,
            includeSidecar: true);
        return true;
    }

    private static bool TryEmitLayerPackers(
        string stem,
        IReadOnlyList<IMethodSymbol> methods,
        IReadOnlyList<ShaderCompilationManifest> manifests,
        out string source,
        out string? reason)
    {
        var parts = new string[methods.Count];
        for (var index = 0; index < methods.Count; index++)
        {
            var layerStem = methods.Count == 1 ? stem : stem + "Layer" + index;
            if (!ArtifactSourceEmitter.TryEmitPackingMethods(
                    methods[index],
                    manifests[index],
                    manifests[index].Stage,
                    out parts[index],
                    out reason,
                    layerStem))
            {
                source = string.Empty;
                return false;
            }
        }

        source = string.Concat(parts);
        reason = null;
        return true;
    }
}
