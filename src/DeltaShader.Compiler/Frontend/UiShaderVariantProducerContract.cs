using System.Collections.Immutable;

namespace Delta.Shader.Compiler.Frontend;

public readonly record struct UiShaderVariantProducerEntry(
    UiShaderVariantKey Key,
    string ProducerSource,
    string GeneratedProgram,
    string VertexSpirv,
    string FragmentSpirv,
    string GeneratedProgramType,
    string VertexAbiAccessor,
    string FragmentAbiAccessor,
    string VertexEntryPoint,
    string FragmentEntryPoint);

public readonly record struct UiShaderVariantProducerValidation(
    int AllowlistedVariants,
    int ValidatedVariants,
    ImmutableArray<string> Diagnostics)
{
    public bool IsValid => Diagnostics.IsDefaultOrEmpty;
}

public static class UiShaderVariantProducerValidator
{
    public static UiShaderVariantProducerValidation Validate(
        IEnumerable<UiShaderVariantKey> allowlistedVariants,
        IEnumerable<UiShaderVariantProducerEntry> producerEntries,
        string producerRoot,
        string artifactRoot)
    {
        if (allowlistedVariants is null)
        {
            throw new ArgumentNullException(nameof(allowlistedVariants));
        }

        if (producerEntries is null)
        {
            throw new ArgumentNullException(nameof(producerEntries));
        }

        if (string.IsNullOrWhiteSpace(producerRoot))
        {
            throw new ArgumentException("A producer root is required.", nameof(producerRoot));
        }

        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            throw new ArgumentException("An artifact root is required.", nameof(artifactRoot));
        }

        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var allowlist = allowlistedVariants.ToImmutableArray();
        var entries = producerEntries.ToImmutableArray();
        var keys = new HashSet<UiShaderVariantKey>();

        foreach (var key in allowlist)
        {
            if (!keys.Add(key))
            {
                diagnostics.Add($"UI shader variant '{key}' is duplicated in the allowlist.");
            }

            if (!UiShaderVariantCatalog.TryValidate(key, out var catalogDiagnostic))
            {
                var message = catalogDiagnostic is null
                    ? "The UI shader variant is not supported by the finite catalog."
                    : catalogDiagnostic.Message;
                diagnostics.Add($"DSH019: {message}");
            }

        }

        foreach (var entry in entries)
        {
            if (!keys.Contains(entry.Key))
            {
                diagnostics.Add($"Producer entry '{entry.Key}' is not present in the finite UI shader allowlist.");
                continue;
            }

            var matchingEntries = entries.Count(candidate => candidate.Key.Equals(entry.Key));
            if (matchingEntries != 1)
            {
                diagnostics.Add($"Producer entry '{entry.Key}' must have exactly one deterministic producer record.");
                continue;
            }

            ValidateEntry(entry, producerRoot, artifactRoot, diagnostics);
        }

        var validatedVariants = 0;
        foreach (var key in allowlist.Distinct())
        {
            var matchingEntries = entries.Where(entry => entry.Key.Equals(key)).ToArray();
            if (matchingEntries.Length == 0)
            {
                diagnostics.Add($"No concrete producer source/artifact entry is registered for UI shader variant '{key}'.");
            }
            else if (matchingEntries.Length == 1 && UiShaderVariantCatalog.TryValidate(key, out _))
            {
                validatedVariants++;
            }
        }

        return new UiShaderVariantProducerValidation(
            allowlist.Length,
            validatedVariants,
            diagnostics.ToImmutable());
    }

    private static void ValidateEntry(
        UiShaderVariantProducerEntry entry,
        string producerRoot,
        string artifactRoot,
        ImmutableArray<string>.Builder diagnostics)
    {
        ValidateSourceFile(entry.ProducerSource, producerRoot, ".cs", "producer source", diagnostics);
        ValidateGeneratedProgram(entry, producerRoot, diagnostics);
        ValidateArtifactFile(entry.VertexSpirv, artifactRoot, "vertex", diagnostics);
        ValidateArtifactFile(entry.FragmentSpirv, artifactRoot, "fragment", diagnostics);

        if (string.IsNullOrWhiteSpace(entry.VertexEntryPoint))
        {
            diagnostics.Add($"Producer entry '{entry.Key}' has no vertex entry point.");
        }

        if (string.IsNullOrWhiteSpace(entry.FragmentEntryPoint))
        {
            diagnostics.Add($"Producer entry '{entry.Key}' has no fragment entry point.");
        }
    }

    private static void ValidateGeneratedProgram(
        UiShaderVariantProducerEntry entry,
        string producerRoot,
        ImmutableArray<string>.Builder diagnostics)
    {
        var path = ResolvePath(producerRoot, entry.GeneratedProgram, "generated program", entry.Key, diagnostics);
        if (path is null)
        {
            return;
        }

        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            diagnostics.Add($"UI shader variant '{entry.Key}' has no generated C# program at '{entry.GeneratedProgram}'.");
            return;
        }

        var generated = File.ReadAllText(path);
        RequireGeneratedSymbol(entry, generated, entry.GeneratedProgramType, "program type", diagnostics);
        RequireGeneratedSymbol(entry, generated, entry.VertexAbiAccessor, "vertex ABI accessor", diagnostics);
        RequireGeneratedSymbol(entry, generated, entry.FragmentAbiAccessor, "fragment ABI accessor", diagnostics);
    }

    private static void RequireGeneratedSymbol(
        UiShaderVariantProducerEntry entry,
        string generated,
        string symbol,
        string description,
        ImmutableArray<string>.Builder diagnostics)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !generated.Contains(symbol, StringComparison.Ordinal))
        {
            diagnostics.Add($"UI shader variant '{entry.Key}' generated program is missing its {description} '{symbol}'.");
        }
    }

    private static void ValidateSourceFile(
        string relativePath,
        string root,
        string extension,
        string description,
        ImmutableArray<string>.Builder diagnostics)
    {
        var path = ResolvePath(root, relativePath, description, default, diagnostics);
        if (path is null || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            diagnostics.Add($"UI shader producer {description} '{relativePath}' is missing or has an invalid extension.");
        }
    }

    private static void ValidateArtifactFile(
        string relativePath,
        string root,
        string stage,
        ImmutableArray<string>.Builder diagnostics)
    {
        var path = ResolvePath(root, relativePath, $"{stage} SPIR-V", default, diagnostics);
        if (path is null || !path.EndsWith(".spv", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            diagnostics.Add($"UI shader producer {stage} SPIR-V '{relativePath}' is missing or has an invalid extension.");
            return;
        }

        if (new FileInfo(path).Length == 0)
        {
            diagnostics.Add($"UI shader producer {stage} SPIR-V '{relativePath}' is empty.");
        }
    }

    private static string? ResolvePath(
        string root,
        string relativePath,
        string description,
        UiShaderVariantKey key,
        ImmutableArray<string>.Builder diagnostics)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            diagnostics.Add(key.Equals(default)
                ? $"UI shader producer {description} path is empty."
                : $"UI shader variant '{key}' has an empty {description} path.");
            return null;
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(key.Equals(default)
                ? $"UI shader producer {description} path '{relativePath}' escapes its root."
                : $"UI shader variant '{key}' {description} path '{relativePath}' escapes its root.");
            return null;
        }

        return fullPath;
    }
}
