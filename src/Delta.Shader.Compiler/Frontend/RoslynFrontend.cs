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
    private readonly Dictionary<ShaderStage, ITypeSymbol?> _stageAttributes;

    public RoslynFrontend(Compilation compilation)
    {
        _compilation = compilation;
        _stageAttributes = new Dictionary<ShaderStage, ITypeSymbol?>
        {
            [ShaderStage.Compute] = compilation.GetTypeByMetadataName(typeof(ComputeShaderAttribute).FullName),
            [ShaderStage.Vertex] = compilation.GetTypeByMetadataName(typeof(VertexShaderAttribute).FullName),
            [ShaderStage.Fragment] = compilation.GetTypeByMetadataName(typeof(FragmentShaderAttribute).FullName)
        };
    }

    public IReadOnlyList<ShaderEntryPointSymbol> FindComputeEntryPoints()
    {
        return FindShaderEntryPoints().Where(entry => entry.Stage == ShaderStage.Compute).ToArray();
    }

    public IReadOnlyList<ShaderEntryPointSymbol> FindShaderEntryPoints()
    {
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

                var stageAttribute = _stageAttributes.FirstOrDefault(pair => pair.Value is not null &&
                    methodSymbol.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, pair.Value)));
                if (stageAttribute.Value is null)
                {
                    continue;
                }

                var attribute = methodSymbol.GetAttributes().First(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, stageAttribute.Value));
                var localSize = stageAttribute.Key == ShaderStage.Compute ? ParseLocalSize(attribute) : (x: 1u, y: 1u, z: 1u);
                var entryPointName = ParseEntryPointName(attribute, stageAttribute.Key)
                    ?? methodSymbol.Name;

                result.Add(new ShaderEntryPointSymbol(entryPointName, methodSymbol, stageAttribute.Key, localSize.x, localSize.y, localSize.z));
            }
        }

        return result;
    }

    private static string? ParseEntryPointName(AttributeData attribute, ShaderStage stage)
    {
        var constructorIndex = stage == ShaderStage.Compute ? 3 : 0;
        return attribute.ConstructorArguments.Length > constructorIndex
            ? attribute.ConstructorArguments[constructorIndex].Value as string
            : null;
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
