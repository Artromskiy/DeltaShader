using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

public sealed class ShaderVisibleTypeIssue
{
    public ShaderVisibleTypeIssue(ISymbol symbol, string message)
    {
        Symbol = symbol;
        Message = message;
    }

    public string Id => ShaderDiagnosticId.DSH010;

    public ISymbol Symbol { get; }

    public string Message { get; }
}

public static class ShaderVisibleTypeValidation
{
    private const string PushConstantAttributeName = "Delta.Shader.PushConstantAttribute";
    private const string LayoutAttributeName = "Delta.Shader.LayoutAttribute";
    private const string InterstageAttributeName = "Delta.Shader.InterstageAttribute";

    public static bool IsContextParameter(IParameterSymbol parameter, Compilation compilation)
    {
        if (parameter is null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (parameter.RefKind != RefKind.In ||
            parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } contextType)
        {
            return false;
        }

        return contextType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic)
            .Any(field => IsContextField(field, compilation));
    }

    public static IReadOnlyList<ShaderVisibleTypeIssue> ValidateContext(
        IParameterSymbol parameter,
        Compilation compilation)
    {
        if (!IsContextParameter(parameter, compilation))
        {
            throw new ArgumentException("The parameter is not a shader context parameter.", nameof(parameter));
        }

        var issues = new List<ShaderVisibleTypeIssue>();
        var contextType = (INamedTypeSymbol)parameter.Type;
        foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            var attributes = field.GetAttributes()
                .Where(attribute => IsContextAttribute(attribute.AttributeClass))
                .ToArray();
            if (attributes.Length == 0 && !IsInterstageField(field, compilation))
            {
                AddIssue(field, $"Shader context field '{field.Name}' must declare a varying payload, storage buffer, push constant, texture, or builtin role.", issues);
                continue;
            }

            if (attributes.Length == 0)
            {
                foreach (var issue in Validate(field.Type, field))
                {
                    issues.Add(issue);
                }

                continue;
            }

            if (attributes.Length > 1)
            {
                AddIssue(field, $"Shader context field '{field.Name}' has more than one shader role attribute.", issues);
                continue;
            }

            var attributeName = attributes[0].AttributeClass?.ToDisplayString();
            var visibleType = field.Type;
            if (attributeName == LayoutAttributeName)
            {
                var bindingAttribute = attributes[0];
                if (bindingAttribute.ConstructorArguments.Length == 1)
                {
                    AddIssue(field, "Vertex-input [Layout(location)] is not valid in a compute context.", issues);
                    continue;
                }

                if (bindingAttribute.ConstructorArguments.Length != 2)
                {
                    AddIssue(field, "Descriptor [Layout(set, binding)] requires constant set and binding arguments.", issues);
                    continue;
                }

                var sampledTextureType = compilation.GetTypeByMetadataName("Delta.Shader.SampledTexture2D");
                if (SymbolEqualityComparer.Default.Equals(field.Type, sampledTextureType))
                {
                    continue;
                }

                visibleType = GetBufferElementType(field.Type, compilation) ?? field.Type;
            }

            foreach (var issue in Validate(visibleType, field))
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    public static IReadOnlyList<ShaderVisibleTypeIssue> Validate(
        ITypeSymbol rootType,
        ISymbol? rootSymbol = null)
    {
        if (rootType is null)
        {
            throw new ArgumentNullException(nameof(rootType));
        }

        var issues = new List<ShaderVisibleTypeIssue>();
        Visit(
            rootType,
            rootSymbol,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            issues);
        return issues;
    }

    public static ITypeSymbol GetVisibleRootType(IParameterSymbol parameter, Compilation compilation)
    {
        if (parameter is null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (parameter.Type is INamedTypeSymbol namedType)
        {
            var readOnlyBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.ReadOnlyStorageBuffer`1");
            var readWriteBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.ReadWriteStorageBuffer`1");
            var sampledTexture2D = compilation.GetTypeByMetadataName(
                "Delta.Shader.SampledTexture2D");

            if (namedType.TypeArguments.Length == 1 &&
                (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readOnlyBuffer) ||
                SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readWriteBuffer))
            )
            {
                return namedType.TypeArguments[0];
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, sampledTexture2D))
            {
                return compilation.GetSpecialType(SpecialType.System_UInt32);
            }
        }

        return parameter.Type;
    }

    public static bool TryFindReferenceType(
        ITypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? referenceType)
        => TryFindReferenceType(type, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out referenceType);

    private static void Visit(
        ITypeSymbol type,
        ISymbol? owner,
        HashSet<INamedTypeSymbol> visited,
        HashSet<INamedTypeSymbol> active,
        List<ShaderVisibleTypeIssue> issues)
    {
        if (type is IArrayTypeSymbol)
        {
            AddIssue(owner ?? type, "Shader-visible arrays are managed reference types and are not supported.", issues);
            return;
        }

        if (type.IsReferenceType ||
            type.TypeKind == TypeKind.Class ||
            type.TypeKind == TypeKind.Interface ||
            type.TypeKind == TypeKind.Delegate ||
            type.SpecialType == SpecialType.System_String ||
            type.SpecialType == SpecialType.System_Object)
        {
            AddIssue(
                owner ?? type,
                $"Shader-visible type '{type.ToDisplayString()}' is a reference type; class, string, and object data are not supported.",
                issues);
            return;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            if (!ShaderEnumSupport.TryMap(type, out _))
            {
                AddIssue(
                    owner ?? type,
                    $"Shader-visible enum '{type.ToDisplayString()}' must use a supported 32-bit int or uint underlying type.",
                    issues);
            }

            return;
        }

        if (type is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Struct)
        {
            return;
        }

        if (active.Contains(namedType))
        {
            AddIssue(owner ?? namedType, $"Shader-visible struct '{namedType.ToDisplayString()}' is recursive.", issues);
            return;
        }

        if (!visited.Add(namedType))
        {
            return;
        }

        active.Add(namedType);
        foreach (var field in namedType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            Visit(field.Type, field, visited, active, issues);
        }

        active.Remove(namedType);
    }

    private static void AddIssue(ISymbol symbol, string message, List<ShaderVisibleTypeIssue> issues)
    {
        if (!issues.Any(issue =>
                SymbolEqualityComparer.Default.Equals(issue.Symbol, symbol) &&
                string.Equals(issue.Message, message, StringComparison.Ordinal)))
        {
            issues.Add(new ShaderVisibleTypeIssue(symbol, message));
        }
    }

    private static bool IsContextAttribute(ITypeSymbol? attributeType)
    {
        var name = attributeType?.ToDisplayString();
        return name == PushConstantAttributeName || name == LayoutAttributeName || name == InterstageAttributeName;
    }

    private static bool IsContextField(IFieldSymbol field, Compilation compilation)
        => field.GetAttributes().Any(attribute => IsContextAttribute(attribute.AttributeClass)) ||
            IsInterstageField(field, compilation);

    private static bool IsInterstageField(IFieldSymbol field, Compilation compilation)
        => field.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == InterstageAttributeName) ||
            field.Type is INamedTypeSymbol payloadType &&
            payloadType.GetMembers().OfType<IFieldSymbol>().Any(payloadField =>
                !payloadField.IsStatic && IsSemanticValueType(payloadField.Type, compilation));

    private static bool IsSemanticValueType(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        string[] semanticTypeNames =
        [
            "Delta.Shader.Position",
            "Delta.Shader.Uv0",
            "Delta.Shader.Uv1",
            "Delta.Shader.Color",
            "Delta.Shader.VertexColor",
            "Delta.Shader.FragmentColor",
            "Delta.Shader.WorldPosition",
            "Delta.Shader.WorldNormal",
            "Delta.Shader.Tangent",
            "Delta.Shader.Pixel",
            "Delta.Shader.SegmentRect",
            "Delta.Shader.CornerData",
            "Delta.Shader.BorderWidth"
        ];

        return semanticTypeNames.Any(name =>
            SymbolEqualityComparer.Default.Equals(namedType, compilation.GetTypeByMetadataName(name)));
    }

    private static ITypeSymbol? GetBufferElementType(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var readOnly = compilation.GetTypeByMetadataName("Delta.Shader.ReadOnlyStorageBuffer`1");
        var readWrite = compilation.GetTypeByMetadataName("Delta.Shader.ReadWriteStorageBuffer`1");
        if (namedType.TypeArguments.Length == 1 &&
            (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readOnly) ||
             SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readWrite)))
        {
            return namedType.TypeArguments[0];
        }

        return null;
    }

    private static bool TryFindReferenceType(
        ITypeSymbol type,
        HashSet<INamedTypeSymbol> visiting,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? referenceType)
    {
        if (type is IArrayTypeSymbol || type.IsReferenceType)
        {
            referenceType = type;
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } namedType && visiting.Add(namedType))
        {
            foreach (var field in namedType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
            {
                if (TryFindReferenceType(field.Type, visiting, out referenceType))
                {
                    return true;
                }
            }

            visiting.Remove(namedType);
        }

        referenceType = null;
        return false;
    }
}
