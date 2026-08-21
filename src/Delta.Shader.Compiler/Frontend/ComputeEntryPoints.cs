using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

public static class ComputeEntryPoints
{
    public static ShaderCompilationResult ValidateAndBuild(
        ModuleCompilationContext context,
        RoslynFrontend frontend,
        ShaderCompilationOptions? options = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<ShaderDiagnostic>();
        var entries = frontend.FindComputeEntryPoints();

        if (entries.Count == 0)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "No valid [ComputeShader] entry point found.",
                Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(string.Empty, false, diagnostics);
        }

        if (entries.Count > 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "MVP supports one [ComputeShader] entry point per module.",
                Severity: ShaderDiagnosticSeverity.Error));
        }

        if (!ValidateProfileCompatibility(resultOptions, out var profileError))
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH007,
                profileError!,
                Severity: ShaderDiagnosticSeverity.Error));
        }

        var entry = entries[0];
        var resources = new List<ShaderIrResource>();
        var seenBindings = new HashSet<(uint Set, uint Binding)>();
        var storageBuffers = new Dictionary<IParameterSymbol, uint>(SymbolEqualityComparer.Default);
        var structDefinitions = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);
        IParameterSymbol? invocationParameter = null;

        if (!entry.Method.IsStatic || !entry.Method.ReturnsVoid)
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "[ComputeShader] entry point must be static void.",
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        if (!TryValidateLocalSize(entry, resultOptions, out var localSizeError))
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH007,
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

            var visibleType = ShaderVisibleTypeValidation.GetVisibleRootType(parameter, context.Compilation);
            var visibleTypeIssues = ShaderVisibleTypeValidation.Validate(visibleType, parameter);
            foreach (var issue in visibleTypeIssues)
            {
                diagnostics.Add(CreateDiagnostic(issue.Symbol, issue.Id, issue.Message));
            }

            if (visibleTypeIssues.Count > 0)
            {
                continue;
            }

            var location = parameter.Locations.FirstOrDefault()?.GetLineSpan();
            var attributeInvocationId = parameter.GetAttributes().FirstOrDefault(a =>
                IsGlobalInvocationIdAttribute(a.AttributeClass, context));

            if (attributeInvocationId is not null)
            {
                var invocationLocation = parameter.Locations.FirstOrDefault()?.GetLineSpan();
                if (invocationParameter is not null)
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH002,
                        "Only one [GlobalInvocationId] parameter is supported on a compute entry point.",
                        invocationLocation?.Path,
                        invocationLocation is null ? 0 : invocationLocation.Value.StartLinePosition.Line + 1,
                        invocationLocation is null ? 0 : invocationLocation.Value.StartLinePosition.Character + 1));
                }
                else if (parameter.Type.SpecialType != SpecialType.System_UInt32)
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH002,
                        "[GlobalInvocationId] parameter must be uint.",
                        invocationLocation?.Path,
                        invocationLocation is null ? 0 : invocationLocation.Value.StartLinePosition.Line + 1,
                        invocationLocation is null ? 0 : invocationLocation.Value.StartLinePosition.Character + 1));
                }
                else
                {
                    invocationParameter = parameter;
                }

                continue;
            }

            if (context.SampledTexture2DType is not null &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, context.SampledTexture2DType))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH011,
                    $"Compute entry point parameter '{parameter.Name}' uses SampledTexture2D, but sampled textures are only supported in vertex and fragment stages.",
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                continue;
            }

            if (!TryGetBufferElementType(parameter.Type, context, out var elementType))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH002,
                    $"Compute entry point parameter '{parameter.Name}' type '{parameter.Type}' is not supported in MVP. Use storage-buffer-backed parameter wrappers with explicit [ReadOnlyStorageBuffer] / [ReadWriteStorageBuffer] attributes.",
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                continue;
            }

            if (!TryBuildParameterResource(parameter, context, seenBindings, structDefinitions, out var resource, out var unsupportedReason, out var diagnosticId))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    diagnosticId,
                    unsupportedReason!,
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                continue;
            }

            if (resource is not null)
            {
                storageBuffers[parameter] = resource.Binding;
                resources.Add(resource);
            }
        }

        string? body = string.Empty;
        bool usesBuiltinInvocationId = false;
        if (diagnostics.Count == 0)
        {
            var methodSyntax = entry.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (!TryTranslateExecutableBody(entry.Method, context, methodSyntax, invocationParameter, storageBuffers, out body, out usesBuiltinInvocationId, out var bodyDiagnosticReason, out var bodyDiagnosticId))
            {
                var location = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
                diagnostics.Add(new ShaderDiagnostic(
                    bodyDiagnosticId,
                    bodyDiagnosticReason ?? "Compute entry point body is not supported in MVP.",
                    location?.Path,
                    location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                    location is null ? 0 : location.Value.StartLinePosition.Character + 1));
            }
        }

        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Compute,
            SourceEntryPointName = entry.Name,
            EntryPointName = entry.Name,
            LocalSizeX = entry.LocalSizeX,
            LocalSizeY = entry.LocalSizeY,
            LocalSizeZ = entry.LocalSizeZ,
            Resources = resources,
            Structs = structDefinitions.Values.OrderBy(structure => structure.GlslName, StringComparer.Ordinal).ToArray(),
            Requirements = [$"Vulkan {resultOptions.Profile}", $"GLSL {resultOptions.Glsl}", $"SPIRV {resultOptions.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
            Body = body,
            UsesBuiltinInvocationId = usesBuiltinInvocationId,
            InvocationParameterName = invocationParameter?.Name
        };

        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, resultOptions);
    }

    private static ShaderDiagnostic CreateDiagnostic(ISymbol symbol, string id, string message)
    {
        var location = symbol.Locations.FirstOrDefault()?.GetLineSpan();
        return new ShaderDiagnostic(
            id,
            message,
            location?.Path,
            location is null ? 0 : location.Value.StartLinePosition.Line + 1,
            location is null ? 0 : location.Value.StartLinePosition.Character + 1);
    }

    private static bool TryBuildParameterResource(
        IParameterSymbol parameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out ShaderIrResource? resource,
        out string? unsupportedReason,
        out string diagnosticId)
    {
        resource = null;
        unsupportedReason = null;
        diagnosticId = ShaderDiagnosticId.DSH002;

        if (!TryGetBufferElementType(parameter.Type, context, out var elementType))
        {
            unsupportedReason = $"Unsupported storage buffer wrapper type '{parameter.Type}' in parameter list.";
            return false;
        }

        if (ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out var referenceType))
        {
            unsupportedReason =
                $"Shader-visible storage-buffer type '{elementType}' contains reference type '{referenceType}'. Shader types must contain only value types.";
            diagnosticId = ShaderDiagnosticId.DSH010;
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

        if (!TryMapShaderType(elementType, context, structDefinitions, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var elementGlslType, out var elementLayout, out var members, out unsupportedReason))
        {
            diagnosticId = ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out _)
                ? ShaderDiagnosticId.DSH010
                : elementType.TypeKind == TypeKind.Struct && !context.Intrinsics.TryMapType(elementType, out _)
                ? ShaderDiagnosticId.DSH006
                : ShaderDiagnosticId.DSH002;
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            unsupportedReason =
                $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for '{parameter.Name}'.";
            diagnosticId = ShaderDiagnosticId.DSH005;
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
            Std430Layout = elementLayout,
            Members = members
        };

        return true;
    }

    private static bool TryMapShaderType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        HashSet<INamedTypeSymbol> visiting,
        out string glslType,
        out ShaderStd430Layout layout,
        out IReadOnlyList<ShaderIrStructMember> members,
        out string reason)
    {
        members = Array.Empty<ShaderIrStructMember>();
        if (context.Intrinsics.TryMapType(type, out glslType))
        {
            layout = ShaderStd430Layout.ForGlslType(glslType);
            reason = string.Empty;
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                glslType = "bool";
                break;
            case SpecialType.System_Int32:
                glslType = "int";
                break;
            case SpecialType.System_UInt32:
                glslType = "uint";
                break;
            case SpecialType.System_Single:
                glslType = "float";
                break;
            default:
                if (type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Struct)
                {
                    if (!TryBuildStructLayout(namedType, context, structDefinitions, visiting, out var structure, out reason))
                    {
                        glslType = string.Empty;
                        layout = default!;
                        return false;
                    }

                    glslType = structure.GlslName;
                    layout = ShaderStd430Layout.ForStruct(structure.Alignment, structure.Size);
                    members = structure.Members;
                    return true;
                }

                glslType = string.Empty;
                layout = default!;
                reason = $"Unsupported shader type '{type}'. Shader records must contain only supported unmanaged scalar, vector, matrix, quaternion, or nested record fields.";
                return false;
        }

        layout = ShaderStd430Layout.ForGlslType(glslType);
        reason = string.Empty;
        return true;
    }

    private static bool IsStorageBufferAttribute(
        ITypeSymbol? attributeType,
        ModuleCompilationContext context)
    {
        return SymbolEqualityComparer.Default.Equals(attributeType, context.ReadOnlyStorageBufferAttributeType)
            || SymbolEqualityComparer.Default.Equals(attributeType, context.ReadWriteStorageBufferAttributeType);
    }

    private static bool IsGlobalInvocationIdAttribute(
        ITypeSymbol? attributeType,
        ModuleCompilationContext context)
    {
        return SymbolEqualityComparer.Default.Equals(attributeType, context.GlobalInvocationIdAttributeType);
    }

    private static bool TryTranslateExecutableBody(
        IMethodSymbol method,
        ModuleCompilationContext context,
        MethodDeclarationSyntax? methodSyntax,
        IParameterSymbol? invocationParameter,
        Dictionary<IParameterSymbol, uint> storageParameters,
        out string body,
        out bool usesBuiltinInvocationId,
        out string? reason,
        out string diagnosticId)
    {
        body = string.Empty;
        usesBuiltinInvocationId = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        if (methodSyntax is null)
        {
            reason = "Unable to read compute entry-point source body.";
            return false;
        }

        if (storageParameters.Count == 0)
        {
            body = string.Empty;
            return true;
        }

        var semanticModel = context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree);
        if (!new ComputeShaderBodyTranslator(context.Intrinsics).TryTranslate(method, methodSyntax, semanticModel, invocationParameter, storageParameters, out var translation, out reason, out diagnosticId))
        {
            return false;
        }

        body = translation!.Body;
        usesBuiltinInvocationId = translation.UsesBuiltinInvocationId;
        return true;
    }

    private static bool IsReadOnlyStorageBuffer(ITypeSymbol type, ModuleCompilationContext context)
        => (context.ReadOnlyStorageBufferType is not null &&
            SymbolEqualityComparer.Default.Equals((type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferType)) ||
           (context.ReadOnlyStorageBufferValueType is not null &&
            SymbolEqualityComparer.Default.Equals((type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferValueType));

    private static bool TryBuildStructLayout(
        INamedTypeSymbol type,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        HashSet<INamedTypeSymbol> visiting,
        out ShaderIrStruct structure,
        out string reason)
    {
        if (structDefinitions.TryGetValue(type, out structure!))
        {
            reason = string.Empty;
            return true;
        }

        if (!visiting.Add(type))
        {
            structure = default!;
            reason = $"Recursive shader struct '{type.ToDisplayString()}' is not supported.";
            return false;
        }

        var layoutAttribute = type.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layoutAttribute is not null && layoutAttribute.ConstructorArguments.Length > 0)
        {
            var layoutKind = layoutAttribute.ConstructorArguments[0].Value;
            if (layoutKind is int kind && (kind == 2 || kind == 3))
            {
                visiting.Remove(type);
                structure = default!;
                reason = $"Shader struct '{type.ToDisplayString()}' uses explicit or auto layout; only sequential layout is supported for std430 reflection.";
                return false;
            }
        }

        var members = new List<ShaderIrStructMember>();
        uint offset = 0;
        uint alignment = 1;
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            if (field.Type is IArrayTypeSymbol arrayType && SymbolEqualityComparer.Default.Equals(arrayType.ElementType, type))
            {
                visiting.Remove(type);
                structure = default!;
                reason = $"Recursive shader struct '{type.ToDisplayString()}' through field '{field.Name}' is not supported.";
                return false;
            }

            if (!TryMapShaderType(field.Type, context, structDefinitions, visiting, out var fieldGlslType, out var fieldLayout, out var nestedMembers, out reason))
            {
                visiting.Remove(type);
                structure = default!;
                reason = $"Shader struct field '{type.ToDisplayString()}.{field.Name}' is unsupported: {reason}";
                return false;
            }

            offset = AlignUp(offset, fieldLayout.Alignment);
            members.Add(new ShaderIrStructMember
            {
                Name = field.Name,
                GlslName = "member_" + SanitizeName(field.Name),
                GlslType = fieldGlslType,
                Offset = offset,
                Alignment = fieldLayout.Alignment,
                Size = fieldLayout.Size,
                ArrayStride = fieldLayout.ArrayStride,
                MatrixStride = fieldLayout.MatrixStride,
                Members = nestedMembers
            });
            offset += fieldLayout.Size;
            alignment = Math.Max(alignment, fieldLayout.Alignment);
        }

        if (members.Count == 0)
        {
            visiting.Remove(type);
            structure = default!;
            reason = $"Shader struct '{type.ToDisplayString()}' has no instance data fields.";
            return false;
        }

        var size = AlignUp(offset, alignment);
        structure = new ShaderIrStruct
        {
            Name = type.ToDisplayString(),
            GlslName = "DeltaStruct_" + SanitizeName(type.ToDisplayString()),
            Alignment = alignment,
            Size = size,
            ArrayStride = size,
            Members = members
        };
        structDefinitions[type] = structure;
        visiting.Remove(type);
        reason = string.Empty;
        return true;
    }

    private static uint AlignUp(uint value, uint alignment)
        => alignment == 0 ? value : (value + alignment - 1) / alignment * alignment;

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

        var originalDefinition = namedType.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadOnlyStorageBufferValueType) ||
            SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadWriteStorageBufferValueType))
        {
            elementType = context.Compilation.GetSpecialType(SpecialType.System_UInt32);
            return true;
        }

        if (context.ReadOnlyStorageBufferType is null || context.ReadWriteStorageBufferType is null ||
            (!SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadOnlyStorageBufferType) &&
             !SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadWriteStorageBufferType)))
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

        if (!string.Equals(options.Glsl, "460", StringComparison.Ordinal))
        {
            reason = $"Only Vulkan GLSL 460 is supported; received GLSL '{options.Glsl}'.";
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

        return new string(name
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray());
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
        ReadOnlyStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadOnlyStorageBuffer`1");
        ReadWriteStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadWriteStorageBuffer`1");
        ReadOnlyStorageBufferValueType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadOnlyStorageBuffer");
        ReadWriteStorageBufferValueType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadWriteStorageBuffer");
        SampledTexture2DType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.SampledTexture2D");
        ReadOnlyStorageBufferAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadOnlyStorageBufferAttribute");
        ReadWriteStorageBufferAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ReadWriteStorageBufferAttribute");
        GlobalInvocationIdAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.GlobalInvocationIdAttribute");
        VertexIndexAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.VertexIndexAttribute");
        VertexInputAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.VertexInputAttribute");
        InstanceIndexAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.InstanceIndexAttribute");
        FragmentCoordAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.FragmentCoordAttribute");
        PositionAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.PositionAttribute");
        FragmentColorAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.FragmentColorAttribute");
        ShaderVaryingAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.ShaderVaryingAttribute");
        PushConstantAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.PushConstantAttribute");
        SampledTexture2DAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.Abstractions.SampledTexture2DAttribute");
    }

    public Compilation Compilation { get; }
    public IntrinsicRegistry Intrinsics { get; }
    public ITypeSymbol? ReadOnlyStorageBufferType { get; }
    public ITypeSymbol? ReadWriteStorageBufferType { get; }
    public ITypeSymbol? ReadOnlyStorageBufferValueType { get; }
    public ITypeSymbol? ReadWriteStorageBufferValueType { get; }
    public ITypeSymbol? SampledTexture2DType { get; }
    public ITypeSymbol? ReadOnlyStorageBufferAttributeType { get; }
    public ITypeSymbol? ReadWriteStorageBufferAttributeType { get; }
    public ITypeSymbol? GlobalInvocationIdAttributeType { get; }
    public ITypeSymbol? VertexIndexAttributeType { get; }
    public ITypeSymbol? VertexInputAttributeType { get; }
    public ITypeSymbol? InstanceIndexAttributeType { get; }
    public ITypeSymbol? FragmentCoordAttributeType { get; }
    public ITypeSymbol? PositionAttributeType { get; }
    public ITypeSymbol? FragmentColorAttributeType { get; }
    public ITypeSymbol? ShaderVaryingAttributeType { get; }
    public ITypeSymbol? PushConstantAttributeType { get; }
    public ITypeSymbol? SampledTexture2DAttributeType { get; }
}
