using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler.Intrinsics;

public enum IntrinsicCategory
{
    TypeMapping,
    TypeConstructor,
    Function,
    Operator,
    Swizzle,
    Builtin,
}

public sealed record IntrinsicBinding(
    IntrinsicCategory Category,
    string GlslName,
    string ShaderStage = "compute",
    string? RequiredCapability = null,
    IReadOnlyList<string>? ShaderStages = null)
{
    public bool SupportsStage(ShaderStage stage)
    {
        var stageName = stage.ToString().ToLowerInvariant();
        return ShaderStages is { Count: > 0 }
            ? ShaderStages.Contains(stageName, StringComparer.Ordinal)
            : string.Equals(ShaderStage, "compute", StringComparison.Ordinal)
              || string.Equals(ShaderStage, stageName, StringComparison.Ordinal);
    }
}

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
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        contract ??= ShaderContractManifest.LoadEmbedded();

        var methods = new Dictionary<ISymbol, IntrinsicBinding>(SymbolEqualityComparer.Default);
        var types = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
        var contractTypes = contract.Types
            .Where(type => IsSupportedMapping(type.Mapping) && !string.IsNullOrWhiteSpace(type.GlslName))
            .ToDictionary(type => type.ClrName, StringComparer.Ordinal);

        foreach (var typeContract in contractTypes.Values)
        {
            var type = compilation.GetTypeByMetadataName(contract.GetClrMetadataName(typeContract.ClrName));
            if (type is null)
            {
                continue;
            }

            if (typeContract.GlslName is not { Length: > 0 } glslTypeName)
            {
                continue;
            }

            types[type] = glslTypeName;
            RegisterTypeMembers(methods, type);
        }

        foreach (var functionContract in contract.Functions.Where(function =>
                     IsSupportedMapping(function.Mapping) && !string.IsNullOrWhiteSpace(function.GlslName)))
        {
            var owner = compilation.GetTypeByMetadataName(contract.GetClrMetadataName(functionContract.TypeClrName));
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
                if (functionContract.GlslName is not { Length: > 0 } glslFunctionName)
                {
                    continue;
                }

                methods[method] = new IntrinsicBinding(
                    category,
                    glslFunctionName,
                    RequiredCapability: functionContract.RequiredCapability,
                    ShaderStages: functionContract.Stages);
            }
        }

        RegisterOwnedShaderIntrinsics(methods, compilation);
        RegisterShaderBuiltins(methods, compilation);
        RegisterDeltaMathsFacadeBuiltins(methods, types, compilation, contract);
        return new IntrinsicRegistry(methods, types);
    }

    private static bool IsSupportedMapping(ShaderContractMapping mapping)
        => mapping is ShaderContractMapping.Builtin or ShaderContractMapping.Helper;

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
            !string.Equals(GetContractTypeName(method.ReturnType), contract.ReturnClrName, StringComparison.Ordinal) ||
            method.Parameters.Length != contract.ParameterClrNames.Count)
        {
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (!string.Equals(GetContractTypeName(method.Parameters[index].Type), contract.ParameterClrNames[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetContractTypeName(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_String => "string",
            _ => type.Name
        };

    private static void RegisterOwnedShaderIntrinsics(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        Compilation compilation)
    {
        var intrinsicTypes = new[]
        {
            compilation.GetTypeByMetadataName(typeof(ShaderIntrinsics).FullName),
            compilation.GetTypeByMetadataName(typeof(SampledTexture2D).FullName)
        };

        foreach (var intrinsicType in intrinsicTypes)
        {
            if (intrinsicType is null)
            {
                continue;
            }

            foreach (var method in intrinsicType.GetMembers().OfType<IMethodSymbol>())
            {
                var attribute = method.GetAttributes().FirstOrDefault(candidate =>
                    candidate.AttributeClass?.ToDisplayString() == typeof(ShaderIntrinsicAttribute).FullName);
                if (attribute is null || attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                var glslName = attribute.ConstructorArguments[0].Value as string;
                var stages = GetShaderStages(attribute);
                if (glslName is { Length: > 0 } attributeGlslName && stages.Count > 0)
                {
                    methods[method] = new IntrinsicBinding(
                        IntrinsicCategory.Function,
                        attributeGlslName,
                        stages[0],
                        ShaderStages: stages);
                }
            }
        }
    }

    private static IReadOnlyList<string> GetShaderStages(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2)
        {
            return Array.Empty<string>();
        }

        var stageArgument = attribute.ConstructorArguments[1];
        if (stageArgument.Kind == TypedConstantKind.Array)
        {
            return stageArgument.Values
                .Select(value => value.Value is int stageValue
                    ? ((ShaderStage)stageValue).ToString().ToLowerInvariant()
                    : string.Empty)
                .Where(stage => stage.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return stageArgument.Value is int stageValue
            ? new[] { ((ShaderStage)stageValue).ToString().ToLowerInvariant() }
            : Array.Empty<string>();
    }

    private static void RegisterShaderBuiltins(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        Compilation compilation)
    {
        var builtinType = compilation.GetTypeByMetadataName("Delta.Shader.ShaderBuiltins");
        if (builtinType is null)
        {
            return;
        }

        foreach (var property in builtinType.GetMembers().OfType<IPropertySymbol>())
        {
            var (glslName, stage) = property.Name switch
            {
                "GlobalInvocationId" => ("gl_GlobalInvocationID", ShaderStage.Compute),
                "VertexIndex" => ("gl_VertexIndex", ShaderStage.Vertex),
                "InstanceIndex" => ("gl_InstanceIndex", ShaderStage.Vertex),
                "FragmentCoord" => ("gl_FragCoord", ShaderStage.Fragment),
                _ => (string.Empty, ShaderStage.Compute)
            };
            if (glslName.Length == 0)
            {
                continue;
            }

            var stageName = stage.ToString().ToLowerInvariant();
            methods[property] = new IntrinsicBinding(
                IntrinsicCategory.Builtin,
                glslName,
                stageName,
                ShaderStages: new[] { stageName });

            if (property.Type is not INamedTypeSymbol vectorType)
            {
                continue;
            }

            foreach (var component in vectorType.GetMembers().OfType<IPropertySymbol>()
                         .Where(component => component.Parameters.Length == 0))
            {
                methods[component] = new IntrinsicBinding(
                    IntrinsicCategory.Swizzle,
                    component.Name.ToLowerInvariant(),
                    stageName,
                    ShaderStages: new[] { stageName });
            }
        }
    }

    private static void RegisterDeltaMathsFacadeBuiltins(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        Dictionary<ITypeSymbol, string> mappedTypes,
        Compilation compilation,
        ShaderContractManifest contract)
    {
        var mathsType = compilation.GetTypeByMetadataName(contract.GetClrMetadataName("maths"));
        if (mathsType is null)
        {
            return;
        }

        var glslBuiltins = new HashSet<string>(StringComparer.Ordinal)
        {
            "abs", "acos", "asin", "atan", "ceil", "clamp", "cos", "cross", "distance", "dot",
            "exp", "floor", "length", "max", "min", "mix", "normalize", "pow", "round", "sign",
            "sin", "smoothstep", "smoothStep", "sqrt", "step", "tan"
        };
        var facadeContracts = contract.Functions
            .Where(function => string.Equals(function.TypeClrName, "maths", StringComparison.Ordinal))
            .ToArray();
        foreach (var method in mathsType.GetMembers().OfType<IMethodSymbol>())
        {
            if (!method.IsStatic || method.MethodKind != MethodKind.Ordinary || !glslBuiltins.Contains(method.Name) ||
                !IsGlslValue(method.ReturnType, mappedTypes) || !method.Parameters.All(parameter => IsGlslValue(parameter.Type, mappedTypes)))
            {
                continue;
            }

            var methodContracts = facadeContracts
                .Where(function => string.Equals(function.ClrName, method.Name, StringComparison.Ordinal))
                .ToArray();
            var matchingContract = methodContracts.FirstOrDefault(function => Matches(method, function));
            if (matchingContract is not null)
            {
                if (!IsSupportedMapping(matchingContract.Mapping) || matchingContract.GlslName is not { Length: > 0 } glslName)
                {
                    continue;
                }

                methods[method] = new IntrinsicBinding(IntrinsicCategory.Function, glslName);
                continue;
            }

            methods[method] = new IntrinsicBinding(
                IntrinsicCategory.Function,
                string.Equals(method.Name, "smoothStep", StringComparison.Ordinal) ? "smoothstep" : method.Name);
        }
    }

    private static bool IsGlslValue(ITypeSymbol type, Dictionary<ITypeSymbol, string> mappedTypes)
        => type.SpecialType == SpecialType.System_Single || mappedTypes.ContainsKey(type);

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
        => _methodsAndProperties.TryGetValue(symbol, out binding) ||
           symbol is IMethodSymbol method && _methodsAndProperties.TryGetValue(method.OriginalDefinition, out binding);

    public bool TryMapType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);

    public bool IsDeltaMathsVectorType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);
}
