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
    public static IReadOnlyList<ShaderVisibleTypeIssue> Validate(
        ITypeSymbol rootType,
        ISymbol? rootSymbol = null)
    {
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
        if (parameter.Type is INamedTypeSymbol namedType)
        {
            var readOnlyBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.Abstractions.ReadOnlyStorageBuffer`1");
            var readWriteBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.Abstractions.ReadWriteStorageBuffer`1");
            var readOnlyValueBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.Abstractions.ReadOnlyStorageBuffer");
            var readWriteValueBuffer = compilation.GetTypeByMetadataName(
                "Delta.Shader.Abstractions.ReadWriteStorageBuffer");
            var sampledTexture2D = compilation.GetTypeByMetadataName(
                "Delta.Shader.Abstractions.SampledTexture2D");

            if (namedType.TypeArguments.Length == 1 &&
                (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readOnlyBuffer) ||
                SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, readWriteBuffer))
            )
            {
                return namedType.TypeArguments[0];
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, readOnlyValueBuffer) ||
                SymbolEqualityComparer.Default.Equals(namedType, readWriteValueBuffer))
            {
                return compilation.GetSpecialType(SpecialType.System_UInt32);
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, sampledTexture2D))
            {
                return compilation.GetSpecialType(SpecialType.System_UInt32);
            }
        }

        return parameter.Type;
    }

    public static bool TryFindReferenceType(ITypeSymbol type, out ITypeSymbol referenceType)
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

    private static bool TryFindReferenceType(
        ITypeSymbol type,
        HashSet<INamedTypeSymbol> visiting,
        out ITypeSymbol referenceType)
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

        referenceType = default!;
        return false;
    }
}
