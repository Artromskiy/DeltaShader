using Delta.Shader.Compiler;
using Delta.Shader.Compiler.Frontend;
using Xunit;

namespace DeltaShader.Compiler.Tests;

public sealed class UiShaderEffectVariantHandoffTests
{
    [Fact]
    public void VisualRoundedOuterShadowAndTextSdfGlowRequireDistinctPreparedPairs()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-effects-");
        try
        {
            var visualKey = CreateVisualRoundedOuterShadowKey();
            var textKey = CreateTextSdfGlowKey();
            var visualEntry = CreateEntry(root, visualKey, "visual-rounded-outer-shadow");
            var textEntry = CreateEntry(root, textKey, "text-sdf-glow");

            var validation = UiShaderVariantProducerValidator.Validate(
                [visualKey, textKey],
                [visualEntry, textEntry],
                root.FullName,
                root.FullName);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(2, validation.ValidatedVariants);
            Assert.Empty(validation.MissingVariants);
            Assert.Empty(validation.UnsupportedVariants);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void VisualRoundedInsetShadowAndTextMsdfGlowRequireDistinctPreparedPairs()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-effects-2-");
        try
        {
            var visualKey = CreateVisualRoundedInsetShadowKey();
            var textKey = CreateTextMsdfGlowKey();
            var visualEntry = CreateEntry(root, visualKey, "visual-rounded-inset-shadow");
            var textEntry = CreateEntry(root, textKey, "text-msdf-glow");

            var validation = UiShaderVariantProducerValidator.Validate(
                [visualKey, textKey],
                [visualEntry, textEntry],
                root.FullName,
                root.FullName);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(2, validation.ValidatedVariants);
            Assert.Empty(validation.MissingVariants);
            Assert.Empty(validation.UnsupportedVariants);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SolidAndTextEffectVariantsRequireDistinctPreparedPairs()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-effects-3-");
        try
        {
            var keys = new[]
            {
                CreateVisualSolidStrokeKey(),
                CreateVisualSolidGlowKey(),
                CreateTextMsdfOutlineKey(),
                CreateTextSdfOuterShadowKey()
            };
            var entries = keys
                .Select((key, index) => CreateEntry(root, key, $"effect-variant-{index}"))
                .ToArray();

            var validation = UiShaderVariantProducerValidator.Validate(
                keys,
                entries,
                root.FullName,
                root.FullName);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(4, validation.ValidatedVariants);
            Assert.Empty(validation.MissingVariants);
            Assert.Empty(validation.UnsupportedVariants);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtendedEffectVariantsRequireDistinctPreparedPairs()
    {
        var root = Directory.CreateTempSubdirectory("delta-shader-ui-effects-4-");
        try
        {
            var keys = new[]
            {
                CreateVisualSolidOuterShadowKey(),
                CreateVisualRoundedStrokeOuterShadowKey(),
                CreateTextMsdfOuterShadowKey(),
                CreateTextSdfOutlineOuterShadowGlowKey()
            };
            var entries = keys
                .Select((key, index) => CreateEntry(root, key, $"extended-effect-variant-{index}"))
                .ToArray();

            var validation = UiShaderVariantProducerValidator.Validate(
                keys,
                entries,
                root.FullName,
                root.FullName);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(4, validation.ValidatedVariants);
            Assert.Empty(validation.MissingVariants);
            Assert.Empty(validation.UnsupportedVariants);
            Assert.Equal(4, entries.Select(entry => entry.LayerSetIdentity).Distinct().Count());
            Assert.Equal(4, entries.Select(entry => entry.GeneratedProgram).Distinct().Count());
            Assert.Equal(4, entries.Select(entry => entry.VertexSpirv).Distinct().Count());
            Assert.Equal(4, entries.Select(entry => entry.FragmentSpirv).Distinct().Count());
            Assert.All(entries, entry =>
            {
                Assert.Equal("VertexAbi", entry.VertexAbiAccessor);
                Assert.Equal("FragmentAbi", entry.FragmentAbiAccessor);
                Assert.Equal("PackVertex", entry.VertexPacker);
                Assert.Equal("PackFragment", entry.FragmentPacker);
            });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static UiShaderVariantKey CreateVisualRoundedOuterShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Rounded,
                UiShaderEffectCapabilities.OuterShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextSdfGlowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Sdf,
                UiShaderEffectCapabilities.Glow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateVisualRoundedInsetShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Rounded,
                UiShaderEffectCapabilities.InsetShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextMsdfGlowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Msdf,
                UiShaderEffectCapabilities.Glow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateVisualSolidStrokeKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Solid,
                UiShaderEffectCapabilities.Stroke,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateVisualSolidGlowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Solid,
                UiShaderEffectCapabilities.Glow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateVisualSolidOuterShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Solid,
                UiShaderEffectCapabilities.OuterShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateVisualRoundedStrokeOuterShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateVisual(
                UiShaderPrimitive.Rounded,
                UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextMsdfOutlineKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Msdf,
                UiShaderEffectCapabilities.Outline,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextSdfOuterShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Sdf,
                UiShaderEffectCapabilities.OuterShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextMsdfOuterShadowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Msdf,
                UiShaderEffectCapabilities.OuterShadow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantKey CreateTextSdfOutlineOuterShadowGlowKey()
    {
        Assert.True(
            UiShaderVariantCatalog.TryCreateText(
                UiShaderTextRepresentation.Sdf,
                UiShaderEffectCapabilities.Outline |
                    UiShaderEffectCapabilities.OuterShadow |
                    UiShaderEffectCapabilities.Glow,
                UiShaderQuality.Analytic,
                out var key,
                out var diagnostic),
            diagnostic?.Message);
        return key;
    }

    private static UiShaderVariantProducerEntry CreateEntry(
        DirectoryInfo root,
        UiShaderVariantKey key,
        string identity)
    {
        var source = Path.Combine("sources", identity + ".cs");
        var generated = Path.Combine("generated", identity + ".g.cs");
        var vertex = Path.Combine("artifacts", identity + ".vert.spv");
        var fragment = Path.Combine("artifacts", identity + ".frag.spv");

        foreach (var path in new[] { source, generated, vertex, fragment })
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, Path.GetDirectoryName(path) ?? string.Empty));
        }

        File.WriteAllText(Path.Combine(root.FullName, source), "public static class ProducerShader { }");
        File.WriteAllText(
            Path.Combine(root.FullName, generated),
            "public static class GeneratedProgram { public static object VertexAbi; public static object FragmentAbi; public static object PackVertex; public static object PackFragment; }");
        File.WriteAllBytes(Path.Combine(root.FullName, vertex), [3, 2, 35, 7]);
        File.WriteAllBytes(Path.Combine(root.FullName, fragment), [3, 2, 35, 7]);

        return new UiShaderVariantProducerEntry(
            key,
            source,
            generated,
            vertex,
            fragment,
            "GeneratedProgram",
            "VertexAbi",
            "FragmentAbi",
            "main",
            "main")
        {
            LayerSetIdentity = identity,
            VertexPacker = "PackVertex",
            FragmentPacker = "PackFragment"
        };
    }
}
