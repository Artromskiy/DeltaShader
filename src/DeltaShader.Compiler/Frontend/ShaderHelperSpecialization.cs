using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

internal static class ShaderHelperSpecialization
{
    public static IMethodSymbol ResolveTarget(
        InvocationExpressionSyntax invocation,
        IMethodSymbol called,
        SemanticModel model,
        IMethodSymbol containingMethod)
    {
        if (called.ContainingType.TypeKind != TypeKind.Interface ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return called;
        }

        var receiverType = model.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null)
        {
            return called;
        }

        receiverType = SubstituteTypeParameters(receiverType, containingMethod);
        return receiverType is INamedTypeSymbol concreteType &&
            concreteType.TypeKind == TypeKind.Struct &&
            concreteType.FindImplementationForInterfaceMember(called) is IMethodSymbol implementation
            ? implementation
            : called;
    }

    private static ITypeSymbol SubstituteTypeParameters(ITypeSymbol type, IMethodSymbol containingMethod)
    {
        if (type is ITypeParameterSymbol typeParameter)
        {
            if (typeParameter.ContainingSymbol is INamedTypeSymbol containingType &&
                containingMethod.ContainingType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(containingType, containingMethod.ContainingType.OriginalDefinition) &&
                typeParameter.Ordinal < containingMethod.ContainingType.TypeArguments.Length)
            {
                return containingMethod.ContainingType.TypeArguments[typeParameter.Ordinal];
            }

            if (typeParameter.ContainingSymbol is IMethodSymbol methodDefinition &&
                containingMethod.IsGenericMethod &&
                SymbolEqualityComparer.Default.Equals(methodDefinition, containingMethod.OriginalDefinition) &&
                typeParameter.Ordinal < containingMethod.TypeArguments.Length)
            {
                return containingMethod.TypeArguments[typeParameter.Ordinal];
            }

            return type;
        }

        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
        {
            return type;
        }

        var arguments = namedType.TypeArguments
            .Select(argument => SubstituteTypeParameters(argument, containingMethod))
            .ToArray();
        var unchanged = arguments.Length == namedType.TypeArguments.Length &&
            arguments.Zip(namedType.TypeArguments, (left, right) => SymbolEqualityComparer.Default.Equals(left, right)).All(value => value);
        return unchanged ? type : namedType.OriginalDefinition.Construct(arguments);
    }
}
