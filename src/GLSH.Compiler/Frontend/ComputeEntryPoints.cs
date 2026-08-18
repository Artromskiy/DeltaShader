using System;
using System.Collections.Generic;
using System.Linq;
using DVG.Shaders.Compiler.Intrinsics;
using DVG.Shaders.Compiler.IR;
using DVG.Shaders.Compiler.Syntax;
using Microsoft.CodeAnalysis;

namespace DVG.Shaders.Compiler;

public static class ComputeEntryPoints
{
    public static ShaderCompilationResult ValidateAndBuild(
        ModuleCompilationContext context,
        RoslynFrontend frontend,
        ShaderCompilationOptions? options = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<GlshDiagnostic>();
        var entries = frontend.FindComputeEntryPoints();

        if (entries.Count == 0)
        {
            diagnostics.Add(new GlshDiagnostic(
                GlshDiagnosticId.GLSH004,
                "No valid [ComputeShader] entry point found.",
                Severity: GlshDiagnosticSeverity.Error));
            return new ShaderCompilationResult(string.Empty, false, diagnostics);
        }

        if (entries.Count > 1)
        {
            diagnostics.Add(new GlshDiagnostic(
                GlshDiagnosticId.GLSH004,
                "MVP supports one [ComputeShader] entry point per module.",
                Severity: GlshDiagnosticSeverity.Error));
        }

        if (!ValidateProfileCompatibility(resultOptions, out var profileError))
        {
            diagnostics.Add(new GlshDiagnostic(
                GlshDiagnosticId.GLSH007,
                profileError!,
                Severity: GlshDiagnosticSeverity.Error));
        }

        var entry = entries[0];
        var resources = new List<ShaderIrResource>();
        var seenBindings = new HashSet<(uint Set, uint Binding)>();

        if (!entry.Method.IsStatic || !entry.Method.ReturnsVoid)
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new GlshDiagnostic(
                GlshDiagnosticId.GLSH004,
                "[ComputeShader] entry point must be static void.",
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        if (!TryValidateLocalSize(entry, resultOptions, out var localSizeError))
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new GlshDiagnostic(
                GlshDiagnosticId.GLSH007,
                localSizeError!,
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        foreach (var parameter in entry.Method.Parameters)
        {
            if (parameter.IsImplicitlyDeclared)
            {
                continue;
            }

            var location = parameter.Locations.FirstOrDefault()?.GetLineSpan();
            if (!TryGetBufferElementType(parameter.Type, context, out var elementType))
            {
                diagnostics.Add(new GlshDiagnostic(
                    GlshDiagnosticId.GLSH002,
                    $"Compute entry point parameter '{parameter.Name}' type '{parameter.Type}' is not supported in MVP. Use storage-buffer-backed parameter wrappers with explicit [ReadOnlyStorageBuffer] / [ReadWriteStorageBuffer] attributes.",
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                continue;
            }

            if (!TryBuildParameterResource(parameter, context, seenBindings, out var resource, out var unsupportedReason, out var diagnosticId))
            {
                diagnostics.Add(new GlshDiagnostic(
                    diagnosticId,
                    unsupportedReason!,
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                continue;
            }

            if (resource is not null)
            {
                resources.Add(resource);
            }
        }

        var module = new ShaderIrModule
        {
            EntryPointName = entry.Name,
            LocalSizeX = entry.LocalSizeX,
            LocalSizeY = entry.LocalSizeY,
            LocalSizeZ = entry.LocalSizeZ,
            Resources = resources,
            Requirements = [$"Vulkan {resultOptions.Profile}", $"GLSL {resultOptions.Glsl}", $"SPIRV {resultOptions.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
        };

        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module);
    }

    private static bool TryBuildParameterResource(
        IParameterSymbol parameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        out ShaderIrResource? resource,
        out string? unsupportedReason,
        out string diagnosticId)
    {
        resource = null;
        unsupportedReason = null;
        diagnosticId = GlshDiagnosticId.GLSH002;

        if (!TryMapTypeSupportedForStorageBuffer(parameter.Type, context, out var elementType, out unsupportedReason))
        {
            return false;
        }

        var attribute = parameter.GetAttributes().FirstOrDefault(a =>
            IsStorageBufferAttribute(a.AttributeClass, context));
        if (attribute is null)
        {
            unsupportedReason =
                $"Compute entry point parameter '{parameter.Name}' is not annotated with [ReadOnlyStorageBuffer] or [ReadWriteStorageBuffer].";
            return false;
        }

        var set = GetAttributeUIntArg(attribute, 0);
        var binding = GetAttributeUIntArg(attribute, 1);
        if (!set.HasValue || !binding.HasValue)
        {
            unsupportedReason =
                $"Storage buffer attribute on '{parameter.Name}' must provide set and binding as uint constants.";
            return false;
        }

        if (!TryMapGlslType(elementType, context, out var elementGlslType))
        {
            unsupportedReason =
                $"Unsupported storage buffer element type '{elementType}' in parameter '{parameter.Name}'.";
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            unsupportedReason =
                $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for '{parameter.Name}'.";
            diagnosticId = GlshDiagnosticId.GLSH005;
            return false;
        }

        resource = new ShaderIrResource
        {
            Name = SanitizeName(parameter.Name),
            ParameterName = parameter.Name,
            Category = "storage-buffer",
            Set = key.Set,
            Binding = key.Binding,
            GlslType = elementGlslType,
            ReadOnly = IsReadOnlyStorageBuffer(parameter.Type, context),
            Layout = ShaderStd430Layout.ForGlslType(elementGlslType)
        };

        return true;
    }

    private static bool TryMapTypeSupportedForStorageBuffer(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out ITypeSymbol elementType,
        out string unsupportedReason)
    {
        if (!TryGetBufferElementType(type, context, out elementType))
        {
            unsupportedReason = $"Unsupported storage buffer wrapper type '{type}' in parameter list.";
            return false;
        }

        if (!IsSupportedShaderType(elementType, context, out unsupportedReason))
        {
            return false;
        }

        return true;
    }

    private static bool TryMapGlslType(ITypeSymbol type, ModuleCompilationContext context, out string glslType)
    {
        if (context.Intrinsics.TryMapType(type, out glslType))
        {
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                glslType = "bool";
                return true;
            case SpecialType.System_Int32:
                glslType = "int";
                return true;
            case SpecialType.System_UInt32:
                glslType = "uint";
                return true;
            case SpecialType.System_Single:
                glslType = "float";
                return true;
        }

        glslType = string.Empty;
        return false;
    }

    private static bool IsStorageBufferAttribute(
        ITypeSymbol? attributeType,
        ModuleCompilationContext context)
    {
        return SymbolEqualityComparer.Default.Equals(attributeType, context.ReadOnlyStorageBufferAttributeType)
            || SymbolEqualityComparer.Default.Equals(attributeType, context.ReadWriteStorageBufferAttributeType);
    }

    private static bool IsReadOnlyStorageBuffer(ITypeSymbol type, ModuleCompilationContext context)
        => context.ReadOnlyStorageBufferType is not null &&
            SymbolEqualityComparer.Default.Equals((type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferType);

    private static bool IsSupportedShaderType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out string unsupportedReason)
    {
        unsupportedReason = string.Empty;

        if (type.Name == "fix" ||
            (type.Name.StartsWith("fix", StringComparison.Ordinal) && type.ContainingNamespace?.ToDisplayString() == "DVG.Maths"))
        {
            unsupportedReason = "DVG.Maths.fix is unsupported in MVP. Add explicit float64/fix feature profile to enable.";
            return false;
        }

        if (type.SpecialType is SpecialType.System_Void)
        {
            unsupportedReason = "void is not supported as compute entry-point parameter type.";
            return false;
        }

        if (type.SpecialType is SpecialType.System_Double)
        {
            unsupportedReason = "double is not supported in MVP and requires explicit float64 capability profile.";
            return false;
        }

        if (context.Intrinsics.IsDvgMathsVectorType(type, out _))
        {
            return true;
        }

        if (type.SpecialType is SpecialType.System_Boolean or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Single)
        {
            return true;
        }

        unsupportedReason = $"Unsupported parameter type '{type}' in compute entry point.";
        return false;
    }

    private static bool TryGetBufferElementType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out ITypeSymbol elementType)
    {
        elementType = default!;

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (context.ReadOnlyStorageBufferType is null || context.ReadWriteStorageBufferType is null)
        {
            return false;
        }

        var originalDefinition = namedType.OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadOnlyStorageBufferType) &&
            !SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadWriteStorageBufferType))
        {
            return false;
        }

        if (namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = namedType.TypeArguments[0];
        return true;
    }

    private static uint? GetAttributeUIntArg(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index)
        {
            return null;
        }

        var value = attribute.ConstructorArguments[index];
        return value.Value is uint uintValue ? uintValue : value.Value is int intValue ? (uint)intValue : null;
    }

    private static bool ValidateProfileCompatibility(ShaderCompilationOptions options, out string? reason)
    {
        reason = null;

        if (!TryParseProfileVersion(options.Profile, out var profileVersion))
        {
            reason = $"Unsupported profile '{options.Profile}'.";
            return false;
        }

        if (!Version.TryParse(options.Spirv, out var spirvVersion))
        {
            reason = $"Unsupported SPIR-V version '{options.Spirv}'.";
            return false;
        }

        var maxSpirv = profileVersion >= new Version(1, 3) ? new Version(1, 6) : new Version(1, 5);
        if (profileVersion > new Version(1, 3))
        {
            reason = $"Profile '{options.Profile}' requires additional validation not implemented in this compiler version.";
            return false;
        }

        if (spirvVersion > maxSpirv || spirvVersion < new Version(1, 0))
        {
            reason = $"Profile '{options.Profile}' is incompatible with SPIR-V '{options.Spirv}'. Maximum supported SPIR-V for this profile is {maxSpirv}.";
            return false;
        }

        return true;
    }

    private static bool TryValidateLocalSize(
        ShaderEntryPointSymbol entry,
        ShaderCompilationOptions options,
        out string? error)
    {
        error = null;
        if (!TryParseProfileVersion(options.Profile, out var profileVersion))
        {
            error = $"Unable to validate local size for unsupported profile '{options.Profile}'.";
            return false;
        }

        var profile = profileVersion >= new Version(1, 3) ? 1.3m : 1.2m;
        var maxX = 1024u;
        var maxY = profile >= 1.3m ? 1024u : 1024u;
        var maxZ = profile >= 1.2m ? 64u : 1u;
        var maxInvocations = 1024u;

        if (entry.LocalSizeX == 0 || entry.LocalSizeY == 0 || entry.LocalSizeZ == 0)
        {
            error = "Compute local size dimensions must be positive non-zero values.";
            return false;
        }

        if (entry.LocalSizeX > maxX || entry.LocalSizeY > maxY)
        {
            error = $"Compute local size exceeded target limit: x <= {maxX}, y <= {maxY}.";
            return false;
        }

        if (entry.LocalSizeZ > maxZ)
        {
            error = $"Compute local size exceeded target limit: z <= {maxZ}.";
            return false;
        }

        var invocations = (ulong)entry.LocalSizeX * entry.LocalSizeY * entry.LocalSizeZ;
        if (invocations > maxInvocations)
        {
            error = $"Compute local_size total invocations must not exceed {maxInvocations} for current target profile.";
            return false;
        }

        return true;
    }

    private static bool TryParseProfileVersion(string profile, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        var trimmed = profile.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var number = trimmed.Substring("vulkan".Length);
        if (string.IsNullOrWhiteSpace(number))
        {
            return false;
        }

        if (number.StartsWith(".", StringComparison.Ordinal))
        {
            number = number.TrimStart('.');
        }

        if (number.StartsWith("_"))
        {
            number = number.Substring(1);
        }

        if (!Version.TryParse(number, out version))
        {
            return false;
        }

        return true;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "resource";
        }

        return name.Replace(" ", "_");
    }
}

public sealed class ModuleCompilationContext
{
    public ModuleCompilationContext(Compilation compilation)
        : this(compilation, IntrinsicRegistry.Build(compilation))
    {
    }

    public ModuleCompilationContext(Compilation compilation, IntrinsicRegistry intrinsics)
    {
        Compilation = compilation;
        Intrinsics = intrinsics;
        ReadOnlyStorageBufferType = compilation.GetTypeByMetadataName("DVG.Shaders.Abstractions.ReadOnlyStorageBuffer`1");
        ReadWriteStorageBufferType = compilation.GetTypeByMetadataName("DVG.Shaders.Abstractions.ReadWriteStorageBuffer`1");
        ReadOnlyStorageBufferAttributeType = compilation.GetTypeByMetadataName("DVG.Shaders.Abstractions.ReadOnlyStorageBufferAttribute");
        ReadWriteStorageBufferAttributeType = compilation.GetTypeByMetadataName("DVG.Shaders.Abstractions.ReadWriteStorageBufferAttribute");
    }

    public Compilation Compilation { get; }
    public IntrinsicRegistry Intrinsics { get; }
    public ITypeSymbol? ReadOnlyStorageBufferType { get; }
    public ITypeSymbol? ReadWriteStorageBufferType { get; }
    public ITypeSymbol? ReadOnlyStorageBufferAttributeType { get; }
    public ITypeSymbol? ReadWriteStorageBufferAttributeType { get; }
}
