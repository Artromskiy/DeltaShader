using System;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

internal static class ShaderEnumSupport
{
    public static bool TryMap(ITypeSymbol type, out string glslType)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            glslType = string.Empty;
            return false;
        }

        glslType = enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 => "uint",
            SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 => "int",
            _ => string.Empty
        };

        return glslType.Length != 0;
    }

    public static string FormatConstant(object? value, string glslType)
    {
        if (glslType == "uint")
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "u";
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }
}
