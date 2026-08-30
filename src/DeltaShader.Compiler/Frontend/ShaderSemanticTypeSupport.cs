using Delta.Shader.Compiler.Intrinsics;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

internal static class ShaderSemanticTypeSupport
{
    public static bool TryMapType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        out string glslType)
    {
        if (type is INamedTypeSymbol namedType &&
            context.SemanticValueFields.TryGetValue(namedType, out var valueField))
        {
            return TryMapUnderlyingType(valueField.Type, context, out glslType);
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
            context.SemanticValueFields.TryGetValue(namedType, out var field))
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
            context.SemanticValueFields.TryGetValue(namedType, out var valueField) &&
            SymbolEqualityComparer.Default.Equals(namedType, GetPositionType(context));

    private static ITypeSymbol? GetPositionType(ModuleCompilationContext context)
        => context.Compilation.GetTypeByMetadataName("Delta.Shader.Position");

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
