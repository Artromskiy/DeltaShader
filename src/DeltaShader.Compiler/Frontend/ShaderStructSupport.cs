using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

internal static class ShaderStructSupport
{
    public static bool IsStateless(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Struct &&
           !type.GetMembers().Any(member =>
            member is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } ||
            member is IPropertySymbol property && IsAutoProperty(property));

    public static bool IsAutoProperty(IPropertySymbol property)
    {
        if (property.IsStatic || property.IsIndexer || property.GetMethod is null ||
            property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax syntax)
        {
            return false;
        }

        return syntax.ExpressionBody is null &&
            syntax.AccessorList?.Accessors.Count > 0 &&
            syntax.AccessorList.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null);
    }
}
