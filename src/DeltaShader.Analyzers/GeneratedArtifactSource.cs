using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Analyzers;

internal static class GeneratedArtifactSource
{
    public static string Compute(
        IMethodSymbol method,
        string className,
        string entryPointName,
        string abiFactory,
        string abiAccessor,
        string packingMethods,
        string abiProjection)
    {
        return $$"""
            using System;
            using Delta.Shader.Contract;

            {{Namespace(method)}}

            public static class {{className}}
            {
            {{abiFactory}}
            {{abiAccessor}}
            {{packingMethods}}
                public static ShaderArtifact CreateArtifact(ReadOnlySpan<byte> spirv)
                    => new(spirv, {{Literal(entryPointName)}}, Abi);
            }
            {{abiProjection}}
            """;
    }

    public static string Graphics(
        IMethodSymbol method,
        string className,
        string vertexAbiFactory,
        string fragmentAbiFactory,
        string vertexAbiAccessor,
        string fragmentAbiAccessor,
        string vertexPacking,
        string fragmentPacking,
        string abiProjection)
    {
        return $$"""
            using System;
            using Delta.Shader.Contract;

            {{Namespace(method)}}

            public static class {{className}}
            {
            {{vertexAbiFactory}}
            {{fragmentAbiFactory}}
            {{vertexAbiAccessor}}
            {{fragmentAbiAccessor}}
            {{vertexPacking}}
            {{fragmentPacking}}
                public static IGraphicsShaderProgram CreateProgram(
                    ReadOnlySpan<byte> vertexSpirv,
                    ReadOnlySpan<byte> fragmentSpirv)
                    => new GraphicsShaderProgram(
                        new ShaderArtifact(vertexSpirv, "main", VertexAbi),
                        new ShaderArtifact(fragmentSpirv, "main", FragmentAbi));
            }
            {{abiProjection}}
            """;
    }

    public static string ComputeAbiProjection(IMethodSymbol method, string generatedClassName)
    {
        return $$"""
            public static partial class ShaderAbis
            {
                public static partial class {{Identifier(method.ContainingType.Name)}}
                {
                    public static ShaderAbi {{Identifier(method.Name)}} => {{generatedClassName}}.Abi;
                }
            }
            """;
    }

    public static string GraphicsAbiProjection(
        IMethodSymbol method,
        string generatedClassName,
        string propertyPrefix)
    {
        var prefix = Identifier(propertyPrefix);
        var vertexProperty = prefix.Length == 0 ? "Vertex" : prefix + "Vertex";
        var fragmentProperty = prefix.Length == 0 ? "Fragment" : prefix + "Fragment";
        return $$"""
            public static partial class ShaderAbis
            {
                public static partial class {{Identifier(method.ContainingType.Name)}}
                {
                    public static ShaderAbi {{vertexProperty}} => {{generatedClassName}}.VertexAbi;
                    public static ShaderAbi {{fragmentProperty}} => {{generatedClassName}}.FragmentAbi;
                }
            }
            """;
    }

    private static string Namespace(IMethodSymbol method)
        => method.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : $"namespace {method.ContainingNamespace.ToDisplayString()};";

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string Identifier(string value)
        => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } identifier
            ? identifier
            : string.Empty;
}
