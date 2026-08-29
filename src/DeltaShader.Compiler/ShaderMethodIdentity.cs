using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

internal static class ShaderMethodIdentity
{
    public static string Get(IMethodSymbol method)
    {
        var parameters = string.Join(
            ",",
            method.Parameters.Select(parameter =>
                parameter.RefKind + ":" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        return method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." + method.Name + "`" + method.Arity + "(" + parameters + ")";
    }
}
