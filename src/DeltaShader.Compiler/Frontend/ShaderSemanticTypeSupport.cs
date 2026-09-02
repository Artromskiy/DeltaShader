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
            IsSemanticTypeName(namedType) &&
            namedType.GetMembers("Value").OfType<IFieldSymbol>().SingleOrDefault() is IFieldSymbol field)
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
            IsSemanticTypeName(namedType) &&
            string.Equals(namedType.ToDisplayString(), "Delta.Shader.Position", StringComparison.Ordinal);

    private static bool IsSemanticTypeName(INamedTypeSymbol type)
        => type.ToDisplayString() is
            "Delta.Shader.Position" or
            "Delta.Shader.Uv0" or
            "Delta.Shader.Uv1" or
            "Delta.Shader.Color" or
            "Delta.Shader.VertexColor" or
            "Delta.Shader.FragmentColor" or
            "Delta.Shader.WorldPosition" or
            "Delta.Shader.WorldNormal" or
            "Delta.Shader.Tangent" or
            "Delta.Shader.Pixel" or
            "Delta.Shader.SegmentRect" or
            "Delta.Shader.CornerData" or
            "Delta.Shader.CornerRadii" or
            "Delta.Shader.BorderWidth" or
            "Delta.Shader.ClipRect";

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
