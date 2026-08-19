using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler.Intrinsics;

public enum IntrinsicCategory
{
    TypeMapping,
    TypeConstructor,
    Function,
    Operator,
    Swizzle,
}

public sealed record IntrinsicBinding(
    IntrinsicCategory Category,
    string GlslName,
    string ShaderStage = "compute",
    string? RequiredCapability = null);

public sealed class IntrinsicRegistry
{
    private readonly Dictionary<ISymbol, IntrinsicBinding> _methodsAndProperties;
    private readonly Dictionary<ITypeSymbol, string> _types;

    private IntrinsicRegistry(
        Dictionary<ISymbol, IntrinsicBinding> methodsAndProperties,
        Dictionary<ITypeSymbol, string> types)
    {
        _methodsAndProperties = methodsAndProperties;
        _types = types;
    }

    public static IntrinsicRegistry Build(Compilation compilation, ShaderContractManifest? contract = null)
    {
        contract ??= ShaderContractManifest.LoadEmbedded();

        var methods = new Dictionary<ISymbol, IntrinsicBinding>(SymbolEqualityComparer.Default);
        var types = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
        var contractTypes = contract.Types
            .Where(type => IsSupportedMapping(type.Mapping) && !string.IsNullOrWhiteSpace(type.GlslName))
            .ToDictionary(type => type.ClrName, StringComparer.Ordinal);

        foreach (var typeContract in contractTypes.Values)
        {
            var type = compilation.GetTypeByMetadataName(FullName(contract, typeContract.ClrName));
            if (type is null)
            {
                continue;
            }

            types[type] = typeContract.GlslName!;
            RegisterTypeMembers(methods, type);
        }

        foreach (var functionContract in contract.Functions.Where(function =>
                     IsSupportedMapping(function.Mapping) && !string.IsNullOrWhiteSpace(function.GlslName)))
        {
            var owner = compilation.GetTypeByMetadataName(FullName(contract, functionContract.TypeClrName));
            if (owner is null)
            {
                continue;
            }

            foreach (var method in owner.GetMembers().OfType<IMethodSymbol>())
            {
                if (!Matches(method, functionContract))
                {
                    continue;
                }

                var category = method.MethodKind == MethodKind.UserDefinedOperator
                    ? IntrinsicCategory.Operator
                    : IntrinsicCategory.Function;
                methods[method] = new IntrinsicBinding(
                    category,
                    functionContract.GlslName!,
                    RequiredCapability: functionContract.RequiredCapability);
            }
        }

        RegisterScalarMathsBuiltins(methods, compilation);
        return new IntrinsicRegistry(methods, types);
    }

    private static string FullName(ShaderContractManifest contract, string typeName)
        => contract.Namespace + "." + typeName;

    private static bool IsSupportedMapping(string mapping)
        => string.Equals(mapping, "Builtin", StringComparison.Ordinal) ||
           string.Equals(mapping, "Helper", StringComparison.Ordinal);

    private static void RegisterTypeMembers(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            methods[constructor] = new IntrinsicBinding(IntrinsicCategory.TypeConstructor, "constructor");
        }

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (!property.IsIndexer && property.Parameters.Length == 0 && IsKnownSwizzle(property.Name))
            {
                methods[property] = new IntrinsicBinding(IntrinsicCategory.Swizzle, property.Name);
            }
        }

        // Vector type rows carry the ABI mapping. Their generated operator rows may remain
        // Unsupported; keep the source symbols discoverable without claiming a lowering.
        if (type.Name.Length >= 5 &&
            (type.Name.EndsWith("2", StringComparison.Ordinal) ||
             type.Name.EndsWith("3", StringComparison.Ordinal) ||
             type.Name.EndsWith("4", StringComparison.Ordinal)))
        {
            foreach (var op in type.GetMembers().OfType<IMethodSymbol>()
                         .Where(member => member.MethodKind == MethodKind.UserDefinedOperator))
            {
                methods[op] = new IntrinsicBinding(IntrinsicCategory.Operator, op.Name, RequiredCapability: "std430");
            }
        }
    }

    private static bool Matches(IMethodSymbol method, ShaderContractFunction contract)
    {
        if (!string.Equals(method.Name, contract.ClrName, StringComparison.Ordinal) ||
            !string.Equals(method.ReturnType.Name, contract.ReturnClrName, StringComparison.Ordinal) ||
            method.Parameters.Length != contract.ParameterClrNames.Count)
        {
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (!string.Equals(method.Parameters[index].Type.Name, contract.ParameterClrNames[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void RegisterScalarMathsBuiltins(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        Compilation compilation)
    {
        var mathsType = compilation.GetTypeByMetadataName("Delta.Maths.maths");
        if (mathsType is null)
        {
            return;
        }

        foreach (var method in mathsType.GetMembers().OfType<IMethodSymbol>())
        {
            if (!method.IsStatic || method.MethodKind != MethodKind.Ordinary || !IsSupportedMathsBuiltin(method))
            {
                continue;
            }

            methods[method] = new IntrinsicBinding(IntrinsicCategory.Function, method.Name);
        }
    }

    private static bool IsSupportedMathsBuiltin(IMethodSymbol method)
    {
        if (method.Name is not ("sin" or "cos" or "tan" or "dot" or "normalize"))
        {
            return false;
        }

        return IsSupportedFloatFamilyType(method.ReturnType) &&
               method.Parameters.All(parameter => IsSupportedFloatFamilyType(parameter.Type));
    }

    private static bool IsSupportedFloatFamilyType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Single)
        {
            return true;
        }

        return type.ContainingNamespace?.ToDisplayString() == "Delta.Maths" &&
               type.Name.StartsWith("float", StringComparison.Ordinal);
    }

    private static bool IsKnownSwizzle(string name)
    {
        if (name.Length == 1 || name.Length > 4)
        {
            return false;
        }

        return name.All(ch => ch is 'x' or 'y' or 'z' or 'w' or 'r' or 'g' or 'b' or 'a' or 's' or 't' or 'p' or 'q');
    }

    public bool TryGetIntrinsic<TSymbol>(TSymbol symbol, out IntrinsicBinding binding)
        where TSymbol : class, ISymbol
        => _methodsAndProperties.TryGetValue(symbol, out binding);

    public bool TryMapType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);

    public bool IsDeltaMathsVectorType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);
}
