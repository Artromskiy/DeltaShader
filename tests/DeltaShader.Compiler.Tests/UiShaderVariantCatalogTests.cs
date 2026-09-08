using Xunit;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler.Tests;

public sealed class UiShaderVariantCatalogTests
{
    [Fact]
    public void EffectCapabilityMask_IsDenseAndVersioned()
    {
        Assert.Equal(0x01, (byte)UiShaderEffectCapabilities.Stroke);
        Assert.Equal(0x02, (byte)UiShaderEffectCapabilities.OuterShadow);
        Assert.Equal(0x04, (byte)UiShaderEffectCapabilities.InnerShadow);
        Assert.Equal(0x08, (byte)UiShaderEffectCapabilities.OuterGlow);
        Assert.Equal(0x10, (byte)UiShaderEffectCapabilities.InnerGlow);
    }

    [Fact]
    public void VisualVariantHasDeterministicIdentity()
    {
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow,
            UiShaderQuality.Analytic,
            out var first,
            out var firstDiagnostic), firstDiagnostic?.Message);
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow,
            UiShaderQuality.Analytic,
            out var second,
            out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(first, second);
        Assert.Equal("visual/rounded/flatcolor/none/stroke+outerglow/analytic", first.StableName);
    }

    [Fact]
    public void TextVariantSupportsStrokeAndOuterGlow()
    {
        Assert.True(UiShaderVariantCatalog.TryCreateText(
            UiShaderTextRepresentation.Msdf,
            UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow,
            UiShaderQuality.Analytic,
            out var key,
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(UiShaderTarget.Text, key.Target);
        Assert.Equal(UiShaderMaterial.DistanceField, key.Material);
        Assert.Equal("text/solid/distancefield/msdf/stroke+outerglow/analytic", key.StableName);
    }

    [Theory]
    [InlineData(UiShaderPrimitive.Solid, "visual/solid/flatcolor/none/outershadow/shadowonly")]
    [InlineData(UiShaderPrimitive.Rounded, "visual/rounded/flatcolor/none/outershadow/shadowonly")]
    public void ShadowOnlyVisualHasDeterministicIdentity(
        UiShaderPrimitive primitive,
        string stableName)
    {
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            primitive,
            UiShaderEffectCapabilities.OuterShadow,
            UiShaderQuality.ShadowOnly,
            out var key,
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(stableName, key.StableName);
    }

    [Fact]
    public void ShadowOnlyQualityIsNotAvailableForText()
    {
        Assert.False(UiShaderVariantCatalog.TryCreateText(
            UiShaderTextRepresentation.Sdf,
            UiShaderEffectCapabilities.OuterShadow,
            UiShaderQuality.ShadowOnly,
            out _,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
    }

    [Theory]
    [InlineData(UiShaderMaterial.LinearGradient, "lineargradient")]
    [InlineData(UiShaderMaterial.Image, "image")]
    public void ResourceVisualMaterialsHaveDeterministicIdentity(
        UiShaderMaterial material,
        string materialName)
    {
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Solid,
            material,
            UiShaderEffectCapabilities.None,
            UiShaderQuality.Analytic,
            out var key,
            out var diagnostic), diagnostic?.Message);

        Assert.Equal($"visual/solid/{materialName}/none/none/analytic", key.StableName);
    }

    [Theory]
    [InlineData(UiShaderMaterial.LinearGradient, UiShaderEffectCapabilities.Stroke)]
    [InlineData(UiShaderMaterial.LinearGradient, UiShaderEffectCapabilities.OuterGlow)]
    [InlineData(UiShaderMaterial.Image, UiShaderEffectCapabilities.OuterShadow)]
    [InlineData(UiShaderMaterial.Image, UiShaderEffectCapabilities.InnerShadow)]
    public void ResourceVisualMaterialsRejectEffectsWithStableDiagnostic(
        UiShaderMaterial material,
        UiShaderEffectCapabilities effects)
    {
        Assert.False(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Solid,
            material,
            effects,
            UiShaderQuality.Analytic,
            out var key,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
        Assert.Contains(key.StableName, diagnostic.Message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UiShaderMaterial.LinearGradient)]
    [InlineData(UiShaderMaterial.Image)]
    public void ResourceVisualMaterialsRemainSolidOnly(UiShaderMaterial material)
    {
        Assert.False(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            material,
            UiShaderEffectCapabilities.None,
            UiShaderQuality.Analytic,
            out var key,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
        Assert.Contains(key.StableName, diagnostic.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedCombinationProducesPreparationDiagnostic()
    {
        Assert.False(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Solid,
            UiShaderEffectCapabilities.InnerShadow,
            UiShaderQuality.Analytic,
            out _,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
        Assert.Contains("allowlist", diagnostic.Message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UiShaderEffectCapabilities.Stroke)]
    [InlineData(UiShaderEffectCapabilities.OuterGlow)]
    [InlineData(UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow)]
    public void TextCachedMaskRemainsRenderOwned(
        UiShaderEffectCapabilities effects)
    {
        Assert.False(UiShaderVariantCatalog.TryCreateText(
            UiShaderTextRepresentation.Sdf,
            effects,
            UiShaderQuality.CachedMask,
            out _,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
    }

    [Fact]
    public void CompositeDropsUnusedInterstageFields()
    {
        var position = new ShaderIrContextField
        {
            Kind = ShaderIrContextFieldKind.Interstage,
            TypeIdentity = "Delta.Shader.Position",
            ReadGlslName = "positionIn",
            WriteGlslName = "positionOut",
            GlslType = "vec4",
        };
        var color = new ShaderIrContextField
        {
            Kind = ShaderIrContextFieldKind.Interstage,
            TypeIdentity = "Example.VertexColor",
            ReadGlslName = "colorIn",
            WriteGlslName = "colorOut",
            GlslType = "vec4",
        };
        var unused = new ShaderIrContextField
        {
            Kind = ShaderIrContextFieldKind.Interstage,
            TypeIdentity = "Example.Unused",
            ReadGlslName = "unusedIn",
            WriteGlslName = "unusedOut",
            GlslType = "vec2",
        };

        var vertex = Layer(
            ShaderStage.Vertex,
            "vertex",
            contextFields: [position, color, unused],
            body: "colorOut = colorIn;");
        var fragment = Layer(
            ShaderStage.Fragment,
            "fragment",
            contextFields: [color, unused],
            body: "fragColor = colorIn;");

        var composite = ShaderCompiler.ComposeGraphics([vertex], [fragment]);

        Assert.True(composite.Success, string.Join(Environment.NewLine, composite.Diagnostics));
        Assert.DoesNotContain(composite.Vertex!.Outputs, field => field.Name == "unusedIn");
        Assert.DoesNotContain(composite.Fragment!.Inputs, field => field.Name == "unusedIn");
        Assert.Contains(composite.Vertex.Outputs, field => field.GlslType == "vec4" && field.GlslName != "gl_Position");
    }

    [Fact]
    public void InvalidEnumValuesAreRejected()
    {
        var valid = new UiShaderVariantKey(
            UiShaderTarget.Visual,
            UiShaderPrimitive.Rounded,
            UiShaderMaterial.FlatColor,
            UiShaderTextRepresentation.None,
            UiShaderEffectCapabilities.None,
            UiShaderQuality.Analytic);

        AssertInvalid(valid with { Target = (UiShaderTarget)255 });
        AssertInvalid(valid with { Primitive = (UiShaderPrimitive)255 });
        AssertInvalid(valid with { Material = (UiShaderMaterial)255 });
        AssertInvalid(valid with { TextRepresentation = (UiShaderTextRepresentation)255 });
        AssertInvalid(valid with { Effects = (UiShaderEffectCapabilities)(1 << 5) });
        AssertInvalid(valid with { Quality = (UiShaderQuality)255 });
    }

    [Theory]
    [InlineData(UiShaderEffectCapabilities.Stroke)]
    [InlineData(UiShaderEffectCapabilities.OuterShadow)]
    [InlineData(UiShaderEffectCapabilities.InnerShadow)]
    [InlineData(UiShaderEffectCapabilities.OuterGlow)]
    [InlineData(UiShaderEffectCapabilities.InnerGlow)]
    public void RoundedVisualSupportsEveryCanonicalEffect(UiShaderEffectCapabilities effect)
    {
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            effect,
            UiShaderQuality.Analytic,
            out _,
            out var diagnostic), diagnostic?.Message);
    }

    [Theory]
    [InlineData(UiShaderEffectCapabilities.Stroke)]
    [InlineData(UiShaderEffectCapabilities.OuterShadow)]
    [InlineData(UiShaderEffectCapabilities.InnerShadow)]
    [InlineData(UiShaderEffectCapabilities.OuterGlow)]
    [InlineData(UiShaderEffectCapabilities.InnerGlow)]
    public void TextSupportsEveryCanonicalEffect(UiShaderEffectCapabilities effect)
    {
        Assert.True(UiShaderVariantCatalog.TryCreateText(
            UiShaderTextRepresentation.Sdf,
            effect,
            UiShaderQuality.Analytic,
            out _,
            out var diagnostic), diagnostic?.Message);
    }

    [Fact]
    public void PreparationAddsVariantKeyToDeterministicIdentity()
    {
        var key = new UiShaderVariantKey(
            UiShaderTarget.Visual,
            UiShaderPrimitive.Rounded,
            UiShaderMaterial.FlatColor,
            UiShaderTextRepresentation.None,
            UiShaderEffectCapabilities.OuterGlow,
            UiShaderQuality.Analytic);

        var result = ShaderCompiler.PrepareUiVariant(key, [Layer(ShaderStage.Vertex, "vertex")], [Layer(ShaderStage.Fragment, "fragment")]);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Composition);
        Assert.StartsWith(key.StableName + "|ui-composite:", result.VariantIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparationRejectsTransformContextForUiVariant()
    {
        var key = new UiShaderVariantKey(
            UiShaderTarget.Visual,
            UiShaderPrimitive.Solid,
            UiShaderMaterial.FlatColor,
            UiShaderTextRepresentation.None,
            UiShaderEffectCapabilities.None,
            UiShaderQuality.Analytic);
        var vertex = Layer(ShaderStage.Vertex, "vertex", new ShaderIrContextField
        {
            Kind = ShaderIrContextFieldKind.Interstage,
            TypeIdentity = "Delta.Shader.Transform",
            SourcePath = "Transform",
            GlslType = "vec4",
            Stage = ShaderStage.Vertex,
        });

        var result = ShaderCompiler.PrepareUiVariant(key, [vertex], [Layer(ShaderStage.Fragment, "fragment")]);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == ShaderDiagnosticId.DSH019);
        Assert.Contains("transform", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ShaderCompilationResult Layer(
        ShaderStage stage,
        string identity,
        ShaderIrContextField? contextField = null,
        IReadOnlyList<ShaderIrContextField>? contextFields = null,
        string body = "")
        => new(
            identity,
            success: true,
            diagnostics: [],
            module: new ShaderIrModule
            {
                Stage = stage,
                SourceEntryPointName = identity,
                EntryPointName = identity,
                Body = body,
                ContextFields = contextFields ?? (contextField is null ? [] : [contextField]),
            },
            sourceMethodIdentity: identity);

    private static void AssertInvalid(UiShaderVariantKey key)
    {
        Assert.False(UiShaderVariantCatalog.TryValidate(key, out var diagnostic));
        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
    }
}
