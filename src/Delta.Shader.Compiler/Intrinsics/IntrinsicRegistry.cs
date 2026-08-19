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
    int? RequiredCapability = null);

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

    public static IntrinsicRegistry Build(Compilation compilation)
    {
        var methods = new Dictionary<ISymbol, IntrinsicBinding>(SymbolEqualityComparer.Default);
        var types = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

        RegisterType(types, compilation, "Delta.Maths.float2", "vec2");
        RegisterType(types, compilation, "Delta.Maths.float3", "vec3");
        RegisterType(types, compilation, "Delta.Maths.float4", "vec4");
        RegisterType(types, compilation, "Delta.Maths.int2", "ivec2");
        RegisterType(types, compilation, "Delta.Maths.int3", "ivec3");
        RegisterType(types, compilation, "Delta.Maths.int4", "ivec4");
        RegisterType(types, compilation, "Delta.Maths.uint2", "uvec2");
        RegisterType(types, compilation, "Delta.Maths.uint3", "uvec3");
        RegisterType(types, compilation, "Delta.Maths.uint4", "uvec4");
        RegisterType(types, compilation, "Delta.Maths.bool2", "bvec2");
        RegisterType(types, compilation, "Delta.Maths.bool3", "bvec3");
        RegisterType(types, compilation, "Delta.Maths.bool4", "bvec4");

        RegisterVectorMembers(methods, compilation, "Delta.Maths.float2");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.float3");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.float4");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.int2");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.int3");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.int4");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.uint2");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.uint3");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.uint4");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.bool2");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.bool3");
        RegisterVectorMembers(methods, compilation, "Delta.Maths.bool4");

        RegisterMathsMethods(methods, compilation);

        return new IntrinsicRegistry(methods, types);
    }

    private static void RegisterType(Dictionary<ITypeSymbol, string> types, Compilation compilation, string fullyQualifiedType, string glslType)
    {
        var type = compilation.GetTypeByMetadataName(fullyQualifiedType);
        if (type is not null)
        {
            types[type] = glslType;
        }
    }

    private static void RegisterMathsMethods(
        Dictionary<ISymbol, IntrinsicBinding> methods,
        Compilation compilation)
    {
        var mathsType = compilation.GetTypeByMetadataName("Delta.Maths.maths");
        if (mathsType is null)
        {
            return;
        }

        foreach (var member in mathsType.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            if (!member.IsStatic)
            {
                continue;
            }

            var name = member.Name;
            if (name == "sin" || name == "cos" || name == "tan")
            {
                methods[member] = new IntrinsicBinding(IntrinsicCategory.Function, name);
                continue;
            }

            if (name == "dot" || name == "normalize")
            {
                methods[member] = new IntrinsicBinding(IntrinsicCategory.Function, name);
            }
        }
    }

    private static void RegisterVectorMembers(Dictionary<ISymbol, IntrinsicBinding> methods, Compilation compilation, string typeMetadataName)
    {
        var vectorType = compilation.GetTypeByMetadataName(typeMetadataName);
        if (vectorType is null)
        {
            return;
        }

        foreach (var ctor in vectorType.InstanceConstructors)
        {
            methods[ctor] = new IntrinsicBinding(IntrinsicCategory.TypeConstructor, "constructor");
        }

        foreach (var property in vectorType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsIndexer)
            {
                continue;
            }

            if (property.Parameters.Length > 0)
            {
                continue;
            }

            if (!IsKnownSwizzle(property.Name))
            {
                continue;
            }

            methods[property] = new IntrinsicBinding(IntrinsicCategory.Swizzle, property.Name);
        }

        foreach (var op in vectorType.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.UserDefinedOperator))
        {
            methods[op] = new IntrinsicBinding(IntrinsicCategory.Operator, op.Name);
        }
    }

    private static bool IsKnownSwizzle(string name)
    {
        if (name.Length == 1 || name.Length > 4)
        {
            return false;
        }

        return name.All(ch => ch is 'x' or 'y' or 'z' or 'w' or 'r' or 'g' or 'b' or 'a' || ch is 's' or 't' or 'p' or 'q');
    }

    public bool TryGetIntrinsic<TSymbol>(TSymbol symbol, out IntrinsicBinding binding)
        where TSymbol : class, ISymbol
    {
        return _methodsAndProperties.TryGetValue(symbol, out binding);
    }

    public bool TryMapType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);

    public bool IsDeltaMathsVectorType(ITypeSymbol type, out string glslType)
        => _types.TryGetValue(type, out glslType);
}
