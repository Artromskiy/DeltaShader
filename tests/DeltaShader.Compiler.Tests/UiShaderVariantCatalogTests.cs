using Xunit;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler.Tests;

public sealed class UiShaderVariantCatalogTests
{
    [Fact]
    public void VisualVariantHasDeterministicIdentity()
    {
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.Glow,
            UiShaderQuality.Analytic,
            out var first,
            out var firstDiagnostic), firstDiagnostic?.Message);
        Assert.True(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Rounded,
            UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.Glow,
            UiShaderQuality.Analytic,
            out var second,
            out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(first, second);
        Assert.Equal("visual/rounded/flatcolor/none/11/analytic", first.StableName);
    }

    [Fact]
    public void TextVariantSupportsOutlineAndGlow()
    {
        Assert.True(UiShaderVariantCatalog.TryCreateText(
            UiShaderTextRepresentation.Msdf,
            UiShaderEffectCapabilities.Outline | UiShaderEffectCapabilities.Glow,
            UiShaderQuality.Analytic,
            out var key,
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(UiShaderTarget.Text, key.Target);
        Assert.Equal(UiShaderMaterial.DistanceField, key.Material);
        Assert.Equal("text/solid/distancefield/msdf/12/analytic", key.StableName);
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

        Assert.Equal($"visual/solid/{materialName}/none/00/analytic", key.StableName);
    }

    [Theory]
    [InlineData(UiShaderMaterial.LinearGradient, UiShaderEffectCapabilities.Stroke)]
    [InlineData(UiShaderMaterial.LinearGradient, UiShaderEffectCapabilities.Glow)]
    [InlineData(UiShaderMaterial.Image, UiShaderEffectCapabilities.OuterShadow)]
    [InlineData(UiShaderMaterial.Image, UiShaderEffectCapabilities.InsetShadow)]
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

    [Fact]
    public void UnsupportedCombinationProducesPreparationDiagnostic()
    {
        Assert.False(UiShaderVariantCatalog.TryCreateVisual(
            UiShaderPrimitive.Solid,
            UiShaderEffectCapabilities.InsetShadow,
            UiShaderQuality.Analytic,
            out _,
            out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(ShaderDiagnosticId.DSH019, diagnostic!.Id);
        Assert.Contains("allowlist", diagnostic.Message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UiShaderEffectCapabilities.Outline)]
    [InlineData(UiShaderEffectCapabilities.Glow)]
    [InlineData(UiShaderEffectCapabilities.Outline | UiShaderEffectCapabilities.Glow)]
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
        AssertInvalid(valid with { Quality = (UiShaderQuality)255 });
    }

    [Fact]
    public void PreparationAddsVariantKeyToDeterministicIdentity()
    {
        var key = new UiShaderVariantKey(
            UiShaderTarget.Visual,
            UiShaderPrimitive.Rounded,
            UiShaderMaterial.FlatColor,
            UiShaderTextRepresentation.None,
            UiShaderEffectCapabilities.Glow,
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
