using System;
using System.Collections.Generic;

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
    OuterShadow = 1 << 1,
    InnerShadow = 1 << 2,
    OuterGlow = 1 << 3,
    InnerGlow = 1 << 4,
}

/// <summary>Prepared quality tier for a UI effect variant.</summary>
public enum UiShaderQuality : byte
{
    Analytic,
    CachedMask,
    ShadowOnly,
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
            GetStableEffectsName(Effects), "/",
            Quality.ToString().ToLowerInvariant());

    private static string GetStableEffectsName(UiShaderEffectCapabilities effects)
    {
        if (effects == UiShaderEffectCapabilities.None)
        {
            return "none";
        }

        var names = new List<string>(5);
        AddEffectName(names, effects, UiShaderEffectCapabilities.Stroke, "stroke");
        AddEffectName(names, effects, UiShaderEffectCapabilities.OuterShadow, "outershadow");
        AddEffectName(names, effects, UiShaderEffectCapabilities.InnerShadow, "innershadow");
        AddEffectName(names, effects, UiShaderEffectCapabilities.OuterGlow, "outerglow");
        AddEffectName(names, effects, UiShaderEffectCapabilities.InnerGlow, "innerglow");

        const UiShaderEffectCapabilities known = UiShaderEffectCapabilities.Stroke |
            UiShaderEffectCapabilities.OuterShadow |
            UiShaderEffectCapabilities.InnerShadow |
            UiShaderEffectCapabilities.OuterGlow |
            UiShaderEffectCapabilities.InnerGlow;
        var unknown = (byte)((byte)effects & ~(byte)known);
        if (unknown != 0)
        {
            names.Add("invalid-" + unknown.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return string.Join("+", names);
    }

    private static void AddEffectName(
        ICollection<string> names,
        UiShaderEffectCapabilities effects,
        UiShaderEffectCapabilities effect,
        string name)
    {
        if ((effects & effect) != 0)
        {
            names.Add(name);
        }
    }
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
            key.Quality is not (UiShaderQuality.Analytic or UiShaderQuality.CachedMask or UiShaderQuality.ShadowOnly))
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
        if (key.Quality == UiShaderQuality.ShadowOnly)
        {
            return effects == UiShaderEffectCapabilities.OuterShadow &&
                key.Primitive is UiShaderPrimitive.Solid or UiShaderPrimitive.Rounded;
        }

        if (key.Quality == UiShaderQuality.CachedMask)
        {
            return key.Primitive == UiShaderPrimitive.Rounded &&
                effects == UiShaderEffectCapabilities.OuterShadow;
        }

        return key.Primitive switch
        {
            UiShaderPrimitive.Solid => effects == UiShaderEffectCapabilities.None ||
                effects == UiShaderEffectCapabilities.Stroke ||
                effects == UiShaderEffectCapabilities.OuterShadow ||
                effects == UiShaderEffectCapabilities.OuterGlow,
            UiShaderPrimitive.Rounded => effects == UiShaderEffectCapabilities.None ||
                effects == UiShaderEffectCapabilities.Stroke ||
                effects == UiShaderEffectCapabilities.OuterShadow ||
                effects == UiShaderEffectCapabilities.InnerShadow ||
                effects == UiShaderEffectCapabilities.OuterGlow ||
                effects == UiShaderEffectCapabilities.InnerGlow ||
                effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow) ||
                effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow) ||
                effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow |
                    UiShaderEffectCapabilities.OuterGlow),
            _ => false,
        };
    }

    private static bool IsSupportedText(UiShaderVariantKey key)
    {
        if (key.Quality != UiShaderQuality.Analytic)
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
            effects == UiShaderEffectCapabilities.Stroke ||
            effects == UiShaderEffectCapabilities.OuterShadow ||
            effects == UiShaderEffectCapabilities.InnerShadow ||
            effects == UiShaderEffectCapabilities.OuterGlow ||
            effects == UiShaderEffectCapabilities.InnerGlow ||
            effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterGlow) ||
            effects == (UiShaderEffectCapabilities.Stroke | UiShaderEffectCapabilities.OuterShadow |
                UiShaderEffectCapabilities.OuterGlow);
    }
}
