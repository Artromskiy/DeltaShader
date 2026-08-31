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
        string packingMethods)
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
        string fragmentPacking)
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
            """;
    }

    private static string Namespace(IMethodSymbol method)
        => method.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : $"namespace {method.ContainingNamespace.ToDisplayString()};";

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
