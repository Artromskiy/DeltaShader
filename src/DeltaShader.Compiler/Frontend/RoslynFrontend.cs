using System.Collections.Generic;
using System.Linq;
using Delta.Shader;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

public sealed class RoslynFrontend
{
    private readonly Compilation _compilation;
    private readonly Dictionary<ShaderStage, ITypeSymbol?> _stageAttributes;
    private readonly ITypeSymbol? _deltaComputeAttribute;

    public RoslynFrontend(Compilation compilation)
    {
        _compilation = compilation;
        _stageAttributes = new Dictionary<ShaderStage, ITypeSymbol?>
        {
            [ShaderStage.Compute] = compilation.GetTypeByMetadataName(typeof(ComputeShaderAttribute).FullName),
            [ShaderStage.Vertex] = compilation.GetTypeByMetadataName(typeof(VertexShaderAttribute).FullName),
            [ShaderStage.Fragment] = compilation.GetTypeByMetadataName(typeof(FragmentShaderAttribute).FullName)
        };
        _deltaComputeAttribute = compilation.GetTypeByMetadataName(typeof(DeltaComputeAttribute).FullName);
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

                var computeAttribute = methodSymbol.GetAttributes().FirstOrDefault(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _stageAttributes[ShaderStage.Compute]) ||
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _deltaComputeAttribute));
                var stageAttribute = computeAttribute is not null
                    ? (Stage: ShaderStage.Compute, Attribute: computeAttribute)
                    : _stageAttributes
                        .Where(pair => pair.Key != ShaderStage.Compute)
                        .Select(pair => (Stage: pair.Key, Attribute: methodSymbol.GetAttributes().FirstOrDefault(attribute =>
                            pair.Value is not null &&
                            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, pair.Value))))
                        .FirstOrDefault(pair => pair.Attribute is not null);
                if (stageAttribute.Attribute is null)
                {
                    continue;
                }

                var localSize = stageAttribute.Stage == ShaderStage.Compute ? ParseLocalSize(stageAttribute.Attribute) : (x: 1u, y: 1u, z: 1u);
                var entryPointName = ParseEntryPointName(stageAttribute.Attribute, stageAttribute.Stage)
                    ?? methodSymbol.Name;

                result.Add(new ShaderEntryPointSymbol(entryPointName, methodSymbol, stageAttribute.Stage, localSize.x, localSize.y, localSize.z));
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
