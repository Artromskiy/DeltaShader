using System.Collections.Generic;
using System.Linq;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

public sealed class RoslynFrontend
{
    private readonly Compilation _compilation;
    private readonly ITypeSymbol? _computeShaderAttribute;

    public RoslynFrontend(Compilation compilation)
    {
        _compilation = compilation;
        _computeShaderAttribute = compilation.GetTypeByMetadataName(typeof(ComputeShaderAttribute).FullName);
    }

    public IReadOnlyList<ShaderEntryPointSymbol> FindComputeEntryPoints()
    {
        if (_computeShaderAttribute is null)
        {
            return [];
        }

        var result = new List<ShaderEntryPointSymbol>();

        foreach (var tree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(tree);
            if (semanticModel is null)
            {
                continue;
            }

            var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
                if (methodSymbol is null)
                {
                    continue;
                }

                var computeAttribute = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _computeShaderAttribute));

                if (computeAttribute is null)
                {
                    continue;
                }

                var localSize = ParseLocalSize(computeAttribute);
                var entryPointName = computeAttribute.NamedArguments
                    .FirstOrDefault(a => a.Key == nameof(ComputeShaderAttribute.EntryPointName)).Value.Value as string
                    ?? methodSymbol.Name;

                result.Add(new ShaderEntryPointSymbol(entryPointName, methodSymbol, localSize.x, localSize.y, localSize.z));
            }
        }

        return result;
    }

    private static (uint x, uint y, uint z) ParseLocalSize(AttributeData attribute)
    {
        var x = GetArgOrDefault(attribute, 0, 1u);
        var y = GetArgOrDefault(attribute, 1, 1u);
        var z = GetArgOrDefault(attribute, 2, 1u);
        return (x, y, z);
    }

    private static uint GetArgOrDefault(AttributeData attribute, int index, uint defaultValue)
    {
        if (attribute.ConstructorArguments.Length > index)
        {
            var value = attribute.ConstructorArguments[index];
            if (value.Value is not null)
            {
                return (uint)value.Value;
            }
        }

        return defaultValue;
    }
}
