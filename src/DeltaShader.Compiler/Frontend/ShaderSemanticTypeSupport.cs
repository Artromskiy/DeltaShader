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
        if (TryGetValueMember(type, context, out var valueMember))
        {
            var underlyingType = valueMember switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };
            if (underlyingType is not null && TryMapUnderlyingType(underlyingType, context, out glslType))
            {
                return true;
            }
        }

        glslType = string.Empty;
        return false;
    }

    public static bool TryGetValueField(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out IFieldSymbol? valueField)
    {
        if (TryGetValueMember(type, context, out var valueMember) && valueMember is IFieldSymbol field)
        {
            valueField = field;
            return true;
        }

        valueField = null;
        return false;
    }

    public static bool TryGetValueMember(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out ISymbol? valueMember)
    {
        if (type is INamedTypeSymbol namedType &&
            IsSemanticValueType(namedType) &&
            namedType.GetMembers("Value").SingleOrDefault(IsInstanceValueMember) is ISymbol member)
        {
            valueMember = member;
            return true;
        }

        valueMember = null;
        return false;
    }

    public static bool IsPosition(
        ITypeSymbol type,
        ModuleCompilationContext context)
        => type is INamedTypeSymbol namedType &&
            string.Equals(namedType.ToDisplayString(), "Delta.Shader.Position", StringComparison.Ordinal);

    private static bool IsSemanticValueType(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Struct &&
            type.GetMembers("Value").Count(IsInstanceValueMember) == 1;

    private static bool IsInstanceValueMember(ISymbol member)
        => member switch
        {
            IFieldSymbol field => !field.IsStatic,
            IPropertySymbol property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null,
            _ => false
        };

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
