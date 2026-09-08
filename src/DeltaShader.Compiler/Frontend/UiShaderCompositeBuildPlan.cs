using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Delta.Shader.Compiler;

/// <summary>
/// Declarative build input for editor-selected UI shader composites.
/// Layer identities are exact <see cref="ShaderCompilationResult.SourceMethodIdentity"/> values.
/// </summary>
public sealed class UiShaderCompositeBuildPlan
{
    public const string DefaultFileName = "DeltaShaderComposites.json";

    private UiShaderCompositeBuildPlan(
        bool emitLayerPrograms,
        IReadOnlyList<UiShaderCompositeBuildEntry> composites)
    {
        EmitLayerPrograms = emitLayerPrograms;
        Composites = composites;
    }

    public bool EmitLayerPrograms { get; }
    public IReadOnlyList<UiShaderCompositeBuildEntry> Composites { get; }

    public static bool TryParse(
        string source,
        out UiShaderCompositeBuildPlan? plan,
        out IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        var failures = new List<ShaderDiagnostic>();
        plan = null;
        try
        {
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetInt32() != 1)
            {
                AddFailure(failures, "UI composite build plan schema must be 1.");
            }

            var emitLayerPrograms = root.TryGetProperty("emitLayerPrograms", out var emitLayers) &&
                emitLayers.ValueKind == JsonValueKind.True;
            var entries = new List<UiShaderCompositeBuildEntry>();
            if (!root.TryGetProperty("composites", out var composites) || composites.ValueKind != JsonValueKind.Array)
            {
                AddFailure(failures, "UI composite build plan must contain a composites array.");
            }
            else
            {
                foreach (var composite in composites.EnumerateArray())
                {
                    TryReadEntry(composite, entries, failures);
                }
            }

            foreach (var duplicate in entries.GroupBy(entry => entry.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                AddFailure(failures, $"UI composite name '{duplicate.Key}' is duplicated.");
            }

            if (failures.Count == 0)
            {
                plan = new UiShaderCompositeBuildPlan(emitLayerPrograms, entries);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            AddFailure(failures, $"UI composite build plan is invalid: {exception.Message}");
        }

        diagnostics = failures;
        return plan is not null;
    }

    private static void TryReadEntry(
        JsonElement source,
        ICollection<UiShaderCompositeBuildEntry> entries,
        ICollection<ShaderDiagnostic> diagnostics)
    {
        var name = ReadRequiredString(source, "name", diagnostics);
        if (!IsIdentifier(name))
        {
            AddFailure(diagnostics, $"UI composite name '{name}' must be a C# identifier.");
        }

        if (!source.TryGetProperty("key", out var keySource) || keySource.ValueKind != JsonValueKind.Object ||
            !TryReadEnum(keySource, "target", out UiShaderTarget target, diagnostics) ||
            !TryReadEnum(keySource, "primitive", out UiShaderPrimitive primitive, diagnostics) ||
            !TryReadEnum(keySource, "material", out UiShaderMaterial material, diagnostics) ||
            !TryReadEnum(keySource, "textRepresentation", out UiShaderTextRepresentation textRepresentation, diagnostics) ||
            !TryReadEffects(keySource, out var effects, diagnostics) ||
            !TryReadEnum(keySource, "quality", out UiShaderQuality quality, diagnostics))
        {
            return;
        }

        var key = new UiShaderVariantKey(target, primitive, material, textRepresentation, effects, quality);
        if (!UiShaderVariantCatalog.TryValidate(key, out var catalogDiagnostic))
        {
            diagnostics.Add(catalogDiagnostic!);
            return;
        }

        var vertexLayers = ReadLayerIdentities(source, "vertexLayers", diagnostics);
        var fragmentLayers = ReadLayerIdentities(source, "fragmentLayers", diagnostics);
        if (name.Length != 0 && vertexLayers.Count != 0 && fragmentLayers.Count != 0)
        {
            entries.Add(new UiShaderCompositeBuildEntry(name, key, vertexLayers, fragmentLayers));
        }
    }

    private static IReadOnlyList<string> ReadLayerIdentities(
        JsonElement source,
        string propertyName,
        ICollection<ShaderDiagnostic> diagnostics)
    {
        var result = new List<string>();
        if (!source.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            AddFailure(diagnostics, $"UI composite property '{propertyName}' must be a non-empty array.");
            return result;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                AddFailure(diagnostics, $"UI composite property '{propertyName}' contains an empty layer identity.");
                continue;
            }

            result.Add(value.GetString()!);
        }

        if (result.Count == 0)
        {
            AddFailure(diagnostics, $"UI composite property '{propertyName}' must contain at least one layer identity.");
        }

        return result;
    }

    private static string ReadRequiredString(
        JsonElement source,
        string propertyName,
        ICollection<ShaderDiagnostic> diagnostics)
    {
        if (source.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }

        AddFailure(diagnostics, $"UI composite property '{propertyName}' must be a non-empty string.");
        return string.Empty;
    }

    private static bool TryReadEnum<T>(
        JsonElement source,
        string propertyName,
        out T value,
        ICollection<ShaderDiagnostic> diagnostics)
        where T : struct
    {
        var text = ReadRequiredString(source, propertyName, diagnostics);
        if (Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value))
        {
            return true;
        }

        AddFailure(diagnostics, $"UI composite property '{propertyName}' has unsupported value '{text}'.");
        return false;
    }

    private static bool TryReadEffects(
        JsonElement source,
        out UiShaderEffectCapabilities effects,
        ICollection<ShaderDiagnostic> diagnostics)
    {
        var text = ReadRequiredString(source, "effects", diagnostics);
        effects = UiShaderEffectCapabilities.None;
        var tokens = text.Split('|');
        foreach (var token in tokens)
        {
            if (token.Length == 0 || token.Any(character => !char.IsLetter(character)) ||
                !Enum.TryParse(token, true, out UiShaderEffectCapabilities effect) ||
                !Enum.IsDefined(typeof(UiShaderEffectCapabilities), effect) ||
                effect == UiShaderEffectCapabilities.None && tokens.Length != 1)
            {
                AddFailure(diagnostics, $"UI composite property 'effects' has unsupported value '{text}'.");
                return false;
            }

            effects |= effect;
        }

        return true;
    }

    private static bool IsIdentifier(string value)
        => value.Length != 0 &&
            (char.IsLetter(value[0]) || value[0] == '_') &&
            value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static void AddFailure(ICollection<ShaderDiagnostic> diagnostics, string message)
        => diagnostics.Add(new ShaderDiagnostic(
            ShaderDiagnosticId.DSH019,
            message,
            Severity: ShaderDiagnosticSeverity.Error));
}

public sealed class UiShaderCompositeBuildEntry
{
    internal UiShaderCompositeBuildEntry(
        string name,
        UiShaderVariantKey key,
        IReadOnlyList<string> vertexLayers,
        IReadOnlyList<string> fragmentLayers)
    {
        Name = name;
        Key = key;
        VertexLayers = vertexLayers;
        FragmentLayers = fragmentLayers;
    }

    public string Name { get; }
    public UiShaderVariantKey Key { get; }
    public IReadOnlyList<string> VertexLayers { get; }
    public IReadOnlyList<string> FragmentLayers { get; }
    public string GeneratedProgramType => Name + "GraphicsShaderProgram";
    public string VertexSpirvFileName => Name + ".vert.spv";
    public string FragmentSpirvFileName => Name + ".frag.spv";
}

public sealed class UiShaderCompositeBuildPreparation
{
    internal UiShaderCompositeBuildPreparation(
        UiShaderCompositeBuildEntry entry,
        UiShaderVariantPreparationResult? variant,
        IReadOnlyList<ShaderCompilationResult> vertexLayers,
        IReadOnlyList<ShaderCompilationResult> fragmentLayers,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        Entry = entry;
        Variant = variant;
        VertexLayers = vertexLayers;
        FragmentLayers = fragmentLayers;
        Diagnostics = diagnostics;
    }

    public UiShaderCompositeBuildEntry Entry { get; }
    public UiShaderVariantPreparationResult? Variant { get; }
    public IReadOnlyList<ShaderCompilationResult> VertexLayers { get; }
    public IReadOnlyList<ShaderCompilationResult> FragmentLayers { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
    public bool Success => Diagnostics.Count == 0 && Variant?.Success == true;
}

public static class UiShaderCompositeBuildPlanner
{
    public static UiShaderCompositeBuildPreparation Prepare(
        UiShaderCompositeBuildEntry entry,
        IReadOnlyList<ShaderCompilationResult> compilationResults)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (compilationResults is null)
        {
            throw new ArgumentNullException(nameof(compilationResults));
        }

        var diagnostics = new List<ShaderDiagnostic>();
        var vertexLayers = Select(entry.VertexLayers, ShaderStage.Vertex, compilationResults, diagnostics);
        var fragmentLayers = Select(entry.FragmentLayers, ShaderStage.Fragment, compilationResults, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new UiShaderCompositeBuildPreparation(entry, null, vertexLayers, fragmentLayers, diagnostics);
        }

        var variant = UiShaderVariantPreparer.Prepare(entry.Key, vertexLayers, fragmentLayers);
        diagnostics.AddRange(variant.Diagnostics);
        return new UiShaderCompositeBuildPreparation(entry, variant, vertexLayers, fragmentLayers, diagnostics);
    }

    private static IReadOnlyList<ShaderCompilationResult> Select(
        IReadOnlyList<string> identities,
        ShaderStage stage,
        IReadOnlyList<ShaderCompilationResult> compilationResults,
        ICollection<ShaderDiagnostic> diagnostics)
    {
        var selected = new List<ShaderCompilationResult>(identities.Count);
        foreach (var identity in identities)
        {
            var matches = compilationResults.Where(result =>
                string.Equals(result.SourceMethodIdentity, identity, StringComparison.Ordinal) &&
                result.Module?.Stage == stage).ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH019,
                    $"UI composite {stage.ToString().ToLowerInvariant()} layer '{identity}' matched {matches.Length} shader entry points; exactly one full symbol identity is required.",
                    Severity: ShaderDiagnosticSeverity.Error));
                continue;
            }

            selected.Add(matches[0]);
        }

        return selected;
    }
}
