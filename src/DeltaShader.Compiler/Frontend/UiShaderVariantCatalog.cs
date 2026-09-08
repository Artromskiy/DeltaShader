using System;

namespace Delta.Shader.Compiler;

/// <summary>Prepared UI shader target selected during composition.</summary>
public enum UiShaderTarget : byte
{
    Visual,
    Text,
}

/// <summary>Base visual primitive used by a prepared UI variant.</summary>
public enum UiShaderPrimitive : byte
{
    Solid,
    Rounded,
}

/// <summary>Material implementation selected by a prepared UI variant.</summary>
public enum UiShaderMaterial : byte
{
    FlatColor,
    DistanceField,
    LinearGradient,
    Image,
}

/// <summary>Text representation selected by a prepared UI variant.</summary>
public enum UiShaderTextRepresentation : byte
{
    None,
    Sdf,
    Msdf,
}

/// <summary>Prepared effect capabilities used by the finite UI variant catalog.</summary>
[Flags]
public enum UiShaderEffectCapabilities : byte
{
    None = 0,
    Stroke = 1 << 0,
    Outline = 1 << 1,
    OuterShadow = 1 << 2,
    InsetShadow = 1 << 3,
    Glow = 1 << 4,
}

/// <summary>Prepared quality tier for a UI effect variant.</summary>
public enum UiShaderQuality : byte
{
    Analytic,
    CachedMask,
}

/// <summary>Deterministic key for one finite, prepared UI shader variant.</summary>
public readonly record struct UiShaderVariantKey(
    UiShaderTarget Target,
    UiShaderPrimitive Primitive,
    UiShaderMaterial Material,
    UiShaderTextRepresentation TextRepresentation,
    UiShaderEffectCapabilities Effects,
    UiShaderQuality Quality)
{
    /// <summary>Gets a stable, order-independent artifact identity.</summary>
    public string StableName
        => string.Concat(
            Target == UiShaderTarget.Visual ? "visual" : "text", "/",
            Primitive.ToString().ToLowerInvariant(), "/",
            Material.ToString().ToLowerInvariant(), "/",
            TextRepresentation.ToString().ToLowerInvariant(), "/",
            ((byte)Effects).ToString("X2", System.Globalization.CultureInfo.InvariantCulture), "/",
            Quality.ToString().ToLowerInvariant());
}

/// <summary>
/// Allowlist for UI shader variants. It is used during preparation; runtime receives
/// a previously registered program for the resulting key and never composes layers.
/// </summary>
public static class UiShaderVariantCatalog
{
    public static bool TryCreateVisual(
        UiShaderPrimitive primitive,
        UiShaderEffectCapabilities effects,
        UiShaderQuality quality,
        out UiShaderVariantKey key,
        out ShaderDiagnostic? diagnostic)
        => TryCreateVisual(
            primitive,
            UiShaderMaterial.FlatColor,
            effects,
            quality,
            out key,
            out diagnostic);

    public static bool TryCreateVisual(
        UiShaderPrimitive primitive,
        UiShaderMaterial material,
        UiShaderEffectCapabilities effects,
        UiShaderQuality quality,
        out UiShaderVariantKey key,
        out ShaderDiagnostic? diagnostic)
        => TryCreate(
            new UiShaderVariantKey(
                UiShaderTarget.Visual,
                primitive,
                material,
                UiShaderTextRepresentation.None,
                effects,
                quality),
            out key,
            out diagnostic);

    public static bool TryCreateText(
        UiShaderTextRepresentation representation,
        UiShaderEffectCapabilities effects,
        UiShaderQuality quality,
        out UiShaderVariantKey key,
        out ShaderDiagnostic? diagnostic)
        => TryCreate(
            new UiShaderVariantKey(
                UiShaderTarget.Text,
                UiShaderPrimitive.Solid,
                UiShaderMaterial.DistanceField,
                representation,
                effects,
                quality),
            out key,
            out diagnostic);

    private static bool TryCreate(
        UiShaderVariantKey candidate,
        out UiShaderVariantKey key,
        out ShaderDiagnostic? diagnostic)
    {
        key = candidate;
        return TryValidate(candidate, out diagnostic);
    }

    /// <summary>
    /// Validates a complete prepared variant key without creating a new key.
    /// Preparation and runtime callers use this same allowlist.
    /// </summary>
    public static bool TryValidate(
        UiShaderVariantKey candidate,
        out ShaderDiagnostic? diagnostic)
    {
        diagnostic = IsSupported(candidate)
            ? null
            : new ShaderDiagnostic(
                ShaderDiagnosticId.DSH019,
                $"UI shader variant '{candidate.StableName}' is not in the prepared allowlist.",
                Severity: ShaderDiagnosticSeverity.Error);
        return diagnostic is null;
    }

    private static bool IsSupported(UiShaderVariantKey key)
    {
        if (key.Target is not (UiShaderTarget.Visual or UiShaderTarget.Text) ||
            key.Primitive is not (UiShaderPrimitive.Solid or UiShaderPrimitive.Rounded) ||
            key.Material is not (UiShaderMaterial.FlatColor or UiShaderMaterial.DistanceField or
                UiShaderMaterial.LinearGradient or UiShaderMaterial.Image) ||
            key.TextRepresentation is not (UiShaderTextRepresentation.None or UiShaderTextRepresentation.Sdf or UiShaderTextRepresentation.Msdf) ||
            key.Quality is not (UiShaderQuality.Analytic or UiShaderQuality.CachedMask))
        {
            return false;
        }

        if (key.Quality == UiShaderQuality.CachedMask &&
            (key.Effects & (UiShaderEffectCapabilities.OuterShadow |
                UiShaderEffectCapabilities.InsetShadow | UiShaderEffectCapabilities.Glow)) == 0)
        {
            return false;
        }

        return key.Target switch
        {
            UiShaderTarget.Visual => IsSupportedVisual(key),
            UiShaderTarget.Text => IsSupportedText(key),
            _ => false,
        };
    }

    private static bool IsSupportedVisual(UiShaderVariantKey key)
    {
        if (key.TextRepresentation != UiShaderTextRepresentation.None)
        {
            return false;
        }

        if (key.Material is UiShaderMaterial.LinearGradient or UiShaderMaterial.Image)
        {
            return key.Primitive == UiShaderPrimitive.Solid &&
                key.Effects == UiShaderEffectCapabilities.None &&
                key.Quality == UiShaderQuality.Analytic;
        }

        if (key.Material != UiShaderMaterial.FlatColor)
        {
            return false;
        }

        var effects = key.Effects;
        var supported = effects == UiShaderEffectCapabilities.None ||
            effects == UiShaderEffectCapabilities.Stroke ||
            effects == UiShaderEffectCapabilities.OuterShadow ||
            effects == UiShaderEffectCapabilities.Glow ||
            effects == UiShaderEffectCapabilities.InsetShadow ||
            effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow) ||
            effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.Glow) ||
            effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow |
                UiShaderEffectCapabilities.Glow);
        return supported && (key.Primitive == UiShaderPrimitive.Rounded ||
            !effects.HasFlag(UiShaderEffectCapabilities.InsetShadow));
    }

    private static bool IsSupportedText(UiShaderVariantKey key)
    {
        if (key.Quality == UiShaderQuality.CachedMask)
        {
            return false;
        }

        if (key.Material != UiShaderMaterial.DistanceField ||
            key.Primitive != UiShaderPrimitive.Solid ||
            key.TextRepresentation is not (UiShaderTextRepresentation.Sdf or UiShaderTextRepresentation.Msdf))
        {
            return false;
        }

        var effects = key.Effects;
        return effects == UiShaderEffectCapabilities.None ||
            effects == UiShaderEffectCapabilities.Outline ||
            effects == UiShaderEffectCapabilities.OuterShadow ||
            effects == UiShaderEffectCapabilities.Glow ||
            effects == (UiShaderEffectCapabilities.Outline | UiShaderEffectCapabilities.Glow) ||
            effects == (UiShaderEffectCapabilities.Outline | UiShaderEffectCapabilities.OuterShadow |
                UiShaderEffectCapabilities.Glow);
    }
}
