using Delta.Shader.Compiler.Intrinsics;
using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace Delta.Shader.Compiler;

internal static class ShaderSemanticTypeSupport
{
    public static bool TryMapType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out string glslType)
    {
        if (TryGetValueField(type, context, out var valueField))
        {
            return TryMapUnderlyingType(valueField!.Type, context, out glslType);
        }

        glslType = string.Empty;
        return false;
    }

    public static bool TryGetValueField(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out IFieldSymbol? valueField)
    {
        if (type is INamedTypeSymbol namedType &&
            IsSemanticValueType(namedType) &&
            namedType.GetMembers("Value").OfType<IFieldSymbol>().SingleOrDefault(field => !field.IsStatic) is IFieldSymbol field)
        {
            valueField = field;
            return true;
        }

        valueField = null;
        return false;
    }

    public static bool IsPosition(
        ITypeSymbol type,
        ModuleCompilationContext context)
        => type is INamedTypeSymbol namedType &&
            string.Equals(namedType.ToDisplayString(), "Delta.Shader.Position", StringComparison.Ordinal);

    private static bool IsSemanticValueType(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Struct &&
            string.Equals(type.ContainingNamespace?.ToDisplayString(), "Delta.Shader", StringComparison.Ordinal) &&
            type.GetMembers("Value").OfType<IFieldSymbol>().Count(field => !field.IsStatic) == 1;

    private static bool TryMapUnderlyingType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out string glslType)
    {
        if (ShaderEnumSupport.TryMap(type, out glslType) ||
            context.Intrinsics.TryMapType(type, out glslType))
        {
            return true;
        }

        glslType = type.SpecialType switch
        {
            SpecialType.System_Single => "float",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Boolean => "bool",
            _ => string.Empty
        };
        return glslType.Length != 0;
    }
}
