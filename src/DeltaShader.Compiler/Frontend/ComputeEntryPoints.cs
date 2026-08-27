using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader;
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
        ShaderCompilationOptions? options = null,
        string? entryPointName = null,
        string? entryPointIdentity = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<ShaderDiagnostic>();
        var entries = frontend.FindComputeEntryPoints()
            .Where(entry => (entryPointName is null || entry.Method.Name == entryPointName) &&
                (entryPointIdentity is null || entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == entryPointIdentity))
            .ToArray();

        if (entries.Length == 0)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "No valid [ComputeShader] entry point found.",
                Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(string.Empty, false, diagnostics);
        }

        if (entries.Length > 1)
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
                profileError ?? "The selected shader profile is not compatible with the compiler.",
                Severity: ShaderDiagnosticSeverity.Error));
        }

        var entry = entries[0];
        var resources = new List<ShaderIrResource>();
        var pushConstants = new List<ShaderIrPushConstant>();
        var seenBindings = new HashSet<(uint Set, uint Binding)>();
        var storageBuffers = new Dictionary<ISymbol, uint>(SymbolEqualityComparer.Default);
        var structDefinitions = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);

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
                localSizeError ?? "The compute local size is invalid.",
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        var contextParameter = entry.Method.Parameters.Length == 1 &&
            ShaderVisibleTypeValidation.IsContextParameter(entry.Method.Parameters[0], context.Compilation)
            ? entry.Method.Parameters[0]
            : null;

        if (contextParameter is null)
        {
            diagnostics.Add(CreateDiagnostic(entry.Method, ShaderDiagnosticId.DSH002,
                "[ComputeShader] entry point must have exactly one 'in' shader context parameter."));
        }
        else
        {
            foreach (var issue in ShaderVisibleTypeValidation.ValidateContext(contextParameter, context.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(issue.Symbol, issue.Id, issue.Message));
            }

            if (diagnostics.Count == 0 &&
                !TryBuildContextContract(contextParameter, context, seenBindings, storageBuffers, structDefinitions,
                    resources, pushConstants, out var contextDiagnostic))
            {
                if (contextDiagnostic is not null)
                {
                    diagnostics.Add(contextDiagnostic);
                }
            }
        }

        string body = string.Empty;
        bool usesBuiltinInvocationId = false;
        if (diagnostics.Count == 0)
        {
            var methodSyntax = entry.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (!TryTranslateExecutableBody(entry.Method, context, methodSyntax, contextParameter, null, storageBuffers, out body, out usesBuiltinInvocationId, out var bodyDiagnosticReason, out var bodyDiagnosticId))
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
            InvocationParameterName = null,
            PushConstants = pushConstants
        };

        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, resultOptions, entry.Method.Name, entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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

    private static bool TryBuildContextContract(
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        Dictionary<ISymbol, uint> resourceBindings,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        List<ShaderIrResource> resources,
        List<ShaderIrPushConstant> pushConstants,
        out ShaderDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (contextParameter.Type is not INamedTypeSymbol contextType)
        {
            diagnostic = CreateDiagnostic(contextParameter, ShaderDiagnosticId.DSH002,
                "Shader context parameter must be a user-defined value type.");
            return false;
        }

        var pushMembers = new List<ShaderIrStructMember>();
        uint pushOffset = 0;
        uint pushAlignment = 1;
        foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            var attributes = field.GetAttributes()
                .Where(attribute => IsContextFieldAttribute(attribute.AttributeClass, context))
                .ToArray();
            if (attributes.Length == 0)
            {
                diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                    $"Shader context field '{field.Name}' must declare a resource, push constant, or builtin role.");
                return false;
            }

            if (attributes.Length > 1)
            {
                diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                    $"Shader context field '{field.Name}' has more than one shader role attribute.");
                return false;
            }

            var attribute = attributes[0];
            if (IsLayoutAttribute(attribute.AttributeClass, context))
            {
                if (attribute.ConstructorArguments.Length == 1)
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        $"Vertex-input [Layout(location)] is not valid in compute context field '{field.Name}'.");
                    return false;
                }

                if (attribute.ConstructorArguments.Length != 2)
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        $"Descriptor [Layout(set, binding)] on context field '{field.Name}' requires two constant arguments.");
                    return false;
                }

                if (SymbolEqualityComparer.Default.Equals(field.Type, context.SampledTexture2DType))
                {
                    if (!TryBuildContextTextureResource(field, contextParameter, context, seenBindings,
                            out var texture, out var textureReason))
                    {
                        diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                            textureReason ?? "Unsupported context sampled texture.");
                        return false;
                    }

                    if (texture is null)
                    {
                        diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                            "Sampled texture context field did not produce a resource binding.");
                        return false;
                    }

                    resourceBindings[field] = texture.Binding;
                    resources.Add(texture);
                    continue;
                }

                if (!TryBuildContextStorageResource(field, contextParameter, context, seenBindings,
                        structDefinitions, out var boundResource, out var boundReason, out var boundDiagnosticId))
                {
                    diagnostic = CreateDiagnostic(field, boundDiagnosticId,
                        boundReason ?? "Unsupported context descriptor resource.");
                    return false;
                }

                if (boundResource is not null)
                {
                    resourceBindings[field] = boundResource.Binding;
                    resources.Add(boundResource);
                }

                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, context.PushConstantAttributeType))
            {
                if (!TryMapShaderType(field.Type, context, structDefinitions,
                        new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var glslType,
                        out var layout, out var members, out var reason))
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        reason ?? $"Unsupported push-constant field '{field.Name}'.");
                    return false;
                }

                pushOffset = AlignUp(pushOffset, layout.Alignment);
                pushMembers.Add(new ShaderIrStructMember
                {
                    Name = field.Name,
                    GlslName = "member_" + SanitizeName(field.Name),
                    GlslType = glslType,
                    Offset = pushOffset,
                    Alignment = layout.Alignment,
                    Size = layout.Size,
                    ArrayStride = layout.ArrayStride,
                    MatrixStride = layout.MatrixStride,
                    Members = members
                });
                pushOffset += layout.Size;
                pushAlignment = Math.Max(pushAlignment, layout.Alignment);
            }
        }

        if (pushMembers.Count > 0)
        {
            pushConstants.Add(new ShaderIrPushConstant
            {
                Name = "DeltaPushConstants",
                ParameterName = contextParameter.Name,
                GlslType = "DeltaPushConstants",
                Alignment = pushAlignment,
                Size = AlignUp(pushOffset, pushAlignment),
                Members = pushMembers
            });
        }

        return true;
    }

    private static bool TryBuildContextStorageResource(
        IFieldSymbol field,
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out ShaderIrResource? resource,
        out string? reason,
        out string diagnosticId)
    {
        resource = null;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH002;
        if (!TryGetBufferElementType(field.Type, context, out var elementType))
        {
            reason = $"Context field '{field.Name}' must use a typed storage-buffer wrapper.";
            return false;
        }

        if (ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out var referenceType))
        {
            reason = $"Shader-visible storage-buffer type '{elementType}' contains reference type '{referenceType}'.";
            diagnosticId = ShaderDiagnosticId.DSH010;
            return false;
        }

        var attribute = field.GetAttributes().FirstOrDefault(candidate =>
            IsLayoutAttribute(candidate.AttributeClass, context));
        if (attribute is null)
        {
            reason = $"Storage-buffer field '{field.Name}' requires an explicit binding and access contract.";
            return false;
        }

        var set = GetAttributeUIntArg(attribute, 0);
        var binding = GetAttributeUIntArg(attribute, 1);
        if (!set.HasValue || !binding.HasValue)
        {
            reason = $"Storage-buffer field '{field.Name}' requires constant set and binding arguments.";
            return false;
        }

        if (!TryMapShaderType(elementType, context, structDefinitions,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var elementGlslType,
                out var elementLayout, out var members, out reason))
        {
            diagnosticId = ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out _)
                ? ShaderDiagnosticId.DSH010
                : ShaderDiagnosticId.DSH002;
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            reason = $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for context field '{field.Name}'.";
            diagnosticId = ShaderDiagnosticId.DSH005;
            return false;
        }

        resource = new ShaderIrResource
        {
            Name = field.Name,
            ParameterName = contextParameter.Name + "." + field.Name,
            Category = ShaderResourceKind.StorageBuffer,
            Set = key.Set,
            Binding = key.Binding,
            GlslType = elementGlslType,
            ReadOnly = IsReadOnlyStorageBuffer(field.Type, context),
            Std430Layout = elementLayout,
            Members = members
        };
        return true;
    }

    private static bool TryBuildContextTextureResource(
        IFieldSymbol field,
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        out ShaderIrResource? resource,
        out string? reason)
    {
        resource = null;
        reason = null;
        var attribute = field.GetAttributes().FirstOrDefault(candidate =>
            IsLayoutAttribute(candidate.AttributeClass, context));
        if (attribute is null || attribute.ConstructorArguments.Length != 2)
        {
            reason = $"SampledTexture2D field '{field.Name}' requires [Layout(set, binding)].";
            return false;
        }

        var set = GetAttributeUIntArg(attribute, 0);
        var binding = GetAttributeUIntArg(attribute, 1);
        if (!set.HasValue || !binding.HasValue)
        {
            reason = $"SampledTexture2D field '{field.Name}' requires constant set and binding arguments.";
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            reason = $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for context field '{field.Name}'.";
            return false;
        }

        resource = new ShaderIrResource
        {
            Name = field.Name,
            ParameterName = contextParameter.Name + "." + field.Name,
            Category = ShaderResourceKind.SampledTexture2D,
            Stage = ShaderStage.Compute,
            Set = key.Set,
            Binding = key.Binding,
            GlslType = "sampler2D",
            ReadOnly = true,
            Access = ShaderResourceAccess.ReadOnly,
            Layout = "opaque"
        };
        return true;
    }

    private static bool IsContextFieldAttribute(ITypeSymbol? attributeType, ModuleCompilationContext context)
        => IsLayoutAttribute(attributeType, context) ||
           SymbolEqualityComparer.Default.Equals(attributeType, context.PushConstantAttributeType);

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
                    if (!TryBuildStructLayout(namedType, context, structDefinitions, visiting, out var structure, out reason) || structure is null)
                    {
                        glslType = string.Empty;
                        layout = ShaderStd430Layout.ForGlslType("uint");
                        return false;
                    }

                    glslType = structure.GlslName;
                    layout = ShaderStd430Layout.ForStruct(structure.Alignment, structure.Size);
                    members = structure.Members;
                    return true;
                }

                glslType = string.Empty;
                layout = ShaderStd430Layout.ForGlslType("uint");
                reason = $"Unsupported shader type '{type}'. Shader records must contain only supported unmanaged scalar, vector, matrix, quaternion, or nested record fields.";
                return false;
        }

        layout = ShaderStd430Layout.ForGlslType(glslType);
        reason = string.Empty;
        return true;
    }

    private static bool IsLayoutAttribute(
        ITypeSymbol? attributeType,
        ModuleCompilationContext context)
        => SymbolEqualityComparer.Default.Equals(attributeType, context.LayoutAttributeType);

    private static bool TryTranslateExecutableBody(
        IMethodSymbol method,
        ModuleCompilationContext context,
        MethodDeclarationSyntax? methodSyntax,
        IParameterSymbol? contextParameter,
        IParameterSymbol? invocationParameter,
        Dictionary<ISymbol, uint> storageParameters,
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
        if (!new ComputeShaderBodyTranslator(context.Intrinsics).TryTranslate(method, methodSyntax, semanticModel,
                contextParameter, invocationParameter, storageParameters,
                out var translation, out reason, out diagnosticId))
        {
            return false;
        }

        if (translation is null)
        {
            reason ??= "Unable to translate compute shader body.";
            return false;
        }

        body = translation.Body;
        usesBuiltinInvocationId = translation.UsesBuiltinInvocationId;
        return true;
    }

    private static bool IsReadOnlyStorageBuffer(ITypeSymbol type, ModuleCompilationContext context)
        => context.ReadOnlyStorageBufferType is not null &&
           SymbolEqualityComparer.Default.Equals((type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferType);

    private static bool TryBuildStructLayout(
        INamedTypeSymbol type,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        HashSet<INamedTypeSymbol> visiting,
        out ShaderIrStruct? structure,
        out string reason)
    {
        if (structDefinitions.TryGetValue(type, out var existing))
        {
            structure = existing;
            reason = string.Empty;
            return true;
        }

        if (!visiting.Add(type))
        {
            structure = null;
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
                structure = null;
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
                structure = null;
                reason = $"Recursive shader struct '{type.ToDisplayString()}' through field '{field.Name}' is not supported.";
                return false;
            }

            if (!TryMapShaderType(field.Type, context, structDefinitions, visiting, out var fieldGlslType, out var fieldLayout, out var nestedMembers, out reason))
            {
                visiting.Remove(type);
                structure = null;
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
            structure = null;
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
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        elementType = null;

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var originalDefinition = namedType.OriginalDefinition;
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
        ReadOnlyStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.ReadOnlyStorageBuffer`1");
        ReadWriteStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.ReadWriteStorageBuffer`1");
        SampledTexture2DType = compilation.GetTypeByMetadataName("Delta.Shader.SampledTexture2D");
        LayoutAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.LayoutAttribute");
        PositionAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.PositionAttribute");
        InterstageAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.InterstageAttribute");
        PushConstantAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.PushConstantAttribute");
    }

    public Compilation Compilation { get; }
    public IntrinsicRegistry Intrinsics { get; }
    public ITypeSymbol? ReadOnlyStorageBufferType { get; }
    public ITypeSymbol? ReadWriteStorageBufferType { get; }
    public ITypeSymbol? SampledTexture2DType { get; }
    public ITypeSymbol? LayoutAttributeType { get; }
    public ITypeSymbol? PositionAttributeType { get; }
    public ITypeSymbol? InterstageAttributeType { get; }
    public ITypeSymbol? PushConstantAttributeType { get; }
}
