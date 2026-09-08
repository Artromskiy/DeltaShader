using Delta.Shader;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.Frontend;
using Xunit;

namespace DeltaShader.Compiler.Tests;

public sealed class UiShaderVariantProducerContractTests
{
    [Fact]
    public void ValidatorRequiresEveryAllowlistedVariantAndConcreteGeneratedSurface()
    {
        var key = default(UiShaderVariantKey);
        var validation = UiShaderVariantProducerValidator.Validate(
            [key],
            [],
            Path.GetTempPath(),
            Path.GetTempPath());

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Contains("No concrete producer source/artifact entry", StringComparison.Ordinal));
        Assert.Contains(key, validation.MissingVariants);
    }

    [Fact]
    public void ValidatorAcceptsACompleteProducerEntry()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-producer-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts"));
            File.WriteAllText(Path.Combine(root.FullName, "UiShaders.cs"), "public static class UiShaders { }");
            File.WriteAllText(
                Path.Combine(root.FullName, "GeneratedUiProgram.cs"),
                "public static class GeneratedUiProgram { public static object VertexAbi; public static object FragmentAbi; }");
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "vertex.spv"), [3, 2, 35, 7]);
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "fragment.spv"), [3, 2, 35, 7]);

            var key = default(UiShaderVariantKey);
            var entry = new UiShaderVariantProducerEntry(
                key,
                "UiShaders.cs",
                "GeneratedUiProgram.cs",
                "artifacts/vertex.spv",
                "artifacts/fragment.spv",
                "GeneratedUiProgram",
                "VertexAbi",
                "FragmentAbi",
                "main",
                "main")
            {
                LayerSetIdentity = "default-layer-set"
            };

            var validation = UiShaderVariantProducerValidator.Validate(
                [key],
                [entry],
                root.FullName,
                root.FullName);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(1, validation.AllowlistedVariants);
            Assert.Equal(1, validation.ValidatedVariants);
            Assert.Empty(validation.MissingVariants);
            Assert.Empty(validation.UnsupportedVariants);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ValidatorRejectsUnsupportedKeyWithStableDiagnostic()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-unsupported-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts"));
            File.WriteAllText(Path.Combine(root.FullName, "UiShaders.cs"), "public static class UiShaders { }");
            File.WriteAllText(
                Path.Combine(root.FullName, "GeneratedUiProgram.cs"),
                "public static class GeneratedUiProgram { public static object VertexAbi; public static object FragmentAbi; }");
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "vertex.spv"), [3, 2, 35, 7]);
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "fragment.spv"), [3, 2, 35, 7]);

            var unsupportedKey = new UiShaderVariantKey(
                (UiShaderTarget)255,
                default,
                default,
                default,
                default,
                default);
            var entry = new UiShaderVariantProducerEntry(
                unsupportedKey,
                "UiShaders.cs",
                "GeneratedUiProgram.cs",
                "artifacts/vertex.spv",
                "artifacts/fragment.spv",
                "GeneratedUiProgram",
                "VertexAbi",
                "FragmentAbi",
                "main",
                "main")
            {
                LayerSetIdentity = "unsupported-layer-set"
            };

            var validation = UiShaderVariantProducerValidator.Validate(
                [unsupportedKey],
                [entry],
                root.FullName,
                root.FullName);

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Diagnostics, diagnostic =>
                diagnostic.StartsWith("DSH019:", StringComparison.Ordinal));
            Assert.Equal(0, validation.ValidatedVariants);
            Assert.Contains(unsupportedKey, validation.UnsupportedVariants);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ValidatorRejectsArtifactAliasAcrossVariantKeys()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-alias-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts"));
            File.WriteAllText(Path.Combine(root.FullName, "UiShaders.cs"), "public static class UiShaders { }");
            File.WriteAllText(
                Path.Combine(root.FullName, "GeneratedUiProgram.cs"),
                "public static class GeneratedUiProgram { public static object VertexAbi; public static object FragmentAbi; }");
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "vertex.spv"), [3, 2, 35, 7]);
            File.WriteAllBytes(Path.Combine(root.FullName, "artifacts", "fragment.spv"), [3, 2, 35, 7]);

            var firstKey = default(UiShaderVariantKey);
            var secondKey = new UiShaderVariantKey(
                (UiShaderTarget)255,
                default,
                default,
                default,
                default,
                default);
            var firstEntry = new UiShaderVariantProducerEntry(
                firstKey,
                "UiShaders.cs",
                "GeneratedUiProgram.cs",
                "artifacts/vertex.spv",
                "artifacts/fragment.spv",
                "GeneratedUiProgram",
                "VertexAbi",
                "FragmentAbi",
                "main",
                "main")
            {
                LayerSetIdentity = "first-layer-set"
            };
            var secondEntry = firstEntry with
            {
                Key = secondKey,
                LayerSetIdentity = "second-layer-set"
            };

            var validation = UiShaderVariantProducerValidator.Validate(
                [firstKey, secondKey],
                [firstEntry, secondEntry],
                root.FullName,
                root.FullName);

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Diagnostics, diagnostic =>
                diagnostic.Contains("is aliased by variants", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
