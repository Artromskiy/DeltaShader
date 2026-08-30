using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

internal sealed class ShaderInterstageLeaf
{
    public ShaderInterstageLeaf(IFieldSymbol field, IReadOnlyList<IFieldSymbol> path)
    {
        Field = field;
        Path = path;
        PathName = string.Join("_", path.Select(member => member.Name));
    }

    public IFieldSymbol Field { get; }
    public IReadOnlyList<IFieldSymbol> Path { get; }
    public string PathName { get; }
}

internal static class ShaderInterstageTraversal
{
    public static IReadOnlyList<ShaderInterstageLeaf> Flatten(
        INamedTypeSymbol type,
        ModuleCompilationContext context,
        Action<IFieldSymbol, string>? report = null)
    {
        var leaves = new List<ShaderInterstageLeaf>();
        Visit(type, context, new List<IFieldSymbol>(), new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), leaves, report);
        return leaves;
    }

    public static bool ContainsSemanticLeaf(INamedTypeSymbol type, ModuleCompilationContext context)
        => Flatten(type, context).Count != 0;

    private static void Visit(
        INamedTypeSymbol type,
        ModuleCompilationContext context,
        List<IFieldSymbol> path,
        HashSet<INamedTypeSymbol> activeTypes,
        List<ShaderInterstageLeaf> leaves,
        Action<IFieldSymbol, string>? report)
    {
        if (!activeTypes.Add(type))
        {
            var recursiveField = path.Count == 0 ? null : path[path.Count - 1];
            if (recursiveField is not null)
            {
                report?.Invoke(recursiveField,
                    $"Interstage payload contains a recursive value type '{type.Name}'.");
            }
            return;
        }

        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            path.Add(field);
            if (ShaderSemanticTypeSupport.TryGetValueField(field.Type, context, out _))
            {
                leaves.Add(new ShaderInterstageLeaf(field, path.ToArray()));
            }
            else if (field.Type is INamedTypeSymbol nestedType &&
                nestedType.TypeKind == TypeKind.Struct &&
                !GraphicsEntryPoints.TryMapType(field.Type, context, out _))
            {
                Visit(nestedType, context, path, activeTypes, leaves, report);
            }
            else
            {
                report?.Invoke(field,
                    $"Interstage field '{string.Join(".", path.Select(member => member.Name))}' must use a Delta.Shader semantic type or contain nested semantic fields.");
            }

            path.RemoveAt(path.Count - 1);
        }

        activeTypes.Remove(type);
    }
}
