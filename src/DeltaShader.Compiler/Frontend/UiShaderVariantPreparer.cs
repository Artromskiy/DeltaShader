using System;
using System.Collections.Generic;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler;

/// <summary>Result of preparing one finite UI shader variant.</summary>
public sealed class UiShaderVariantPreparationResult
{
    internal UiShaderVariantPreparationResult(
        UiShaderVariantKey key,
        ShaderCompositeCompilationResult? composition,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        Key = key;
        Composition = composition;
        Diagnostics = diagnostics;
        Success = diagnostics.Count == 0 && composition?.Success == true;
        VariantIdentity = composition is null
            ? key.StableName
            : key.StableName + "|" + composition.VariantIdentity;
    }

    public UiShaderVariantKey Key { get; }
    public ShaderCompositeCompilationResult? Composition { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
    public bool Success { get; }

    /// <summary>
    /// Gets the deterministic prepared identity, including capability key and
    /// ordered source-layer/interface identity.
    /// </summary>
    public string VariantIdentity { get; }
}

/// <summary>
/// Performs build/editor preparation for a finite UI shader variant.
/// Runtime consumers receive the resulting artifact and do not call this API.
/// </summary>
public static class UiShaderVariantPreparer
{
    public static UiShaderVariantPreparationResult Prepare(
        UiShaderVariantKey key,
        IReadOnlyList<ShaderCompilationResult> vertexLayers,
        IReadOnlyList<ShaderCompilationResult> fragmentLayers)
    {
        if (vertexLayers is null)
        {
            throw new ArgumentNullException(nameof(vertexLayers));
        }

        if (fragmentLayers is null)
        {
            throw new ArgumentNullException(nameof(fragmentLayers));
        }

        if (!UiShaderVariantCatalog.TryValidate(key, out var catalogDiagnostic))
        {
            return new UiShaderVariantPreparationResult(
                key,
                null,
                [catalogDiagnostic!]);
        }

        var composition = ShaderCompositeCompiler.Compose(vertexLayers, fragmentLayers);
        var diagnostics = new List<ShaderDiagnostic>(composition.Diagnostics);
        AddTransformDiagnostics(composition.Context.Fields, diagnostics);
        return new UiShaderVariantPreparationResult(key, composition, diagnostics);
    }

    private static void AddTransformDiagnostics(
        IReadOnlyList<ShaderCompositeContextField> fields,
        List<ShaderDiagnostic> diagnostics)
    {
        foreach (var field in fields)
        {
            if (!IsTransformField(field))
            {
                continue;
            }

            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH019,
                $"UI shader variant context field '{field.SourcePath}' exposes transform data; transform is outside the UI variant catalog.",
                Severity: ShaderDiagnosticSeverity.Error));
        }
    }

    private static bool IsTransformField(ShaderCompositeContextField field)
        => field.SourcePath.IndexOf("transform", StringComparison.OrdinalIgnoreCase) >= 0 ||
            field.TypeIdentity.IndexOf("transform", StringComparison.OrdinalIgnoreCase) >= 0;
}
