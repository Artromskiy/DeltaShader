using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Analyzers;

internal static class GeneratedArtifactSource
{
    public static string Compute(
        IMethodSymbol method,
        string className,
        string entryPointName,
        string spirvFileName,
        string abiFactory,
        string abiAccessor,
        string packingMethods,
        string abiProjection,
        string facadeProjection)
    {
        return $$"""
            using System;
            using System.IO;
            using Delta.Shader.Contract;

            {{Namespace(method)}}

            public static class {{className}}
            {
            {{abiFactory}}
            {{abiAccessor}}
            {{packingMethods}}
                private static class Sidecar
                {
                    public static readonly byte[] Spirv = Load({{Literal(spirvFileName)}});

                    private static byte[] Load(string fileName)
                    {
                        var assemblyDirectory = Path.GetDirectoryName(typeof({{className}}).Assembly.Location);
                        var root = string.IsNullOrEmpty(assemblyDirectory)
                            ? AppContext.BaseDirectory
                            : assemblyDirectory;
                        var path = Find(root, fileName);
                        if (path.Length == 0 && !string.Equals(root, AppContext.BaseDirectory, StringComparison.Ordinal))
                        {
                            path = Find(AppContext.BaseDirectory, fileName);
                        }

                        if (path.Length == 0)
                        {
                            throw new FileNotFoundException($"DeltaShader sidecar '{fileName}' was not found.", fileName);
                        }

                        return File.ReadAllBytes(path);
                    }

                    private static string Find(string root, string fileName)
                    {
                        var shaderRoot = Path.Combine(root, "DeltaShader");
                        var assemblyName = Path.GetFileNameWithoutExtension(typeof({{className}}).Assembly.Location);
                        if (assemblyName.Length > 0)
                        {
                            var assemblyPath = Path.Combine(shaderRoot, assemblyName, fileName);
                            if (File.Exists(assemblyPath))
                            {
                                return assemblyPath;
                            }
                        }

                        var directPath = Path.Combine(shaderRoot, fileName);
                        if (File.Exists(directPath))
                        {
                            return directPath;
                        }

                        if (!Directory.Exists(shaderRoot))
                        {
                            return string.Empty;
                        }

                        var matches = Directory.GetFiles(shaderRoot, fileName, SearchOption.AllDirectories);
                        if (matches.Length > 1)
                        {
                            throw new InvalidOperationException($"Multiple DeltaShader sidecars named '{fileName}' were found below '{shaderRoot}'.");
                        }

                        return matches.Length == 1 ? matches[0] : string.Empty;
                    }
                }

                internal static ReadOnlySpan<byte> GetSpirv() => Sidecar.Spirv;

                public static ShaderArtifact CreateArtifact(ReadOnlySpan<byte> spirv)
                    => new(spirv, {{Literal(entryPointName)}}, Abi);

                public static ShaderArtifact CreateArtifact()
                    => CreateArtifact(Sidecar.Spirv);
            }
            {{abiProjection}}
            {{facadeProjection}}
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
        string vertexSpirvFileName,
        string fragmentSpirvFileName,
        string abiProjection,
        string facadeProjection)
    {
        return $$"""
            using System;
            using System.IO;
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
                private static class Sidecar
                {
                    public static readonly byte[] VertexSpirv = Load({{Literal(vertexSpirvFileName)}});
                    public static readonly byte[] FragmentSpirv = Load({{Literal(fragmentSpirvFileName)}});

                    private static byte[] Load(string fileName)
                    {
                        var assemblyDirectory = Path.GetDirectoryName(typeof({{className}}).Assembly.Location);
                        var root = string.IsNullOrEmpty(assemblyDirectory)
                            ? AppContext.BaseDirectory
                            : assemblyDirectory;
                        var path = Find(root, fileName);
                        if (path.Length == 0 && !string.Equals(root, AppContext.BaseDirectory, StringComparison.Ordinal))
                        {
                            path = Find(AppContext.BaseDirectory, fileName);
                        }

                        if (path.Length == 0)
                        {
                            throw new FileNotFoundException($"DeltaShader sidecar '{fileName}' was not found.", fileName);
                        }

                        return File.ReadAllBytes(path);
                    }

                    private static string Find(string root, string fileName)
                    {
                        var shaderRoot = Path.Combine(root, "DeltaShader");
                        var assemblyName = Path.GetFileNameWithoutExtension(typeof({{className}}).Assembly.Location);
                        if (assemblyName.Length > 0)
                        {
                            var assemblyPath = Path.Combine(shaderRoot, assemblyName, fileName);
                            if (File.Exists(assemblyPath))
                            {
                                return assemblyPath;
                            }
                        }

                        var directPath = Path.Combine(shaderRoot, fileName);
                        if (File.Exists(directPath))
                        {
                            return directPath;
                        }

                        if (!Directory.Exists(shaderRoot))
                        {
                            return string.Empty;
                        }

                        var matches = Directory.GetFiles(shaderRoot, fileName, SearchOption.AllDirectories);
                        if (matches.Length > 1)
                        {
                            throw new InvalidOperationException($"Multiple DeltaShader sidecars named '{fileName}' were found below '{shaderRoot}'.");
                        }

                        return matches.Length == 1 ? matches[0] : string.Empty;
                    }
                }

                internal static ReadOnlySpan<byte> GetVertexSpirv() => Sidecar.VertexSpirv;
                internal static ReadOnlySpan<byte> GetFragmentSpirv() => Sidecar.FragmentSpirv;

                public static IGraphicsShaderProgram CreateProgram(
                    ReadOnlySpan<byte> vertexSpirv,
                    ReadOnlySpan<byte> fragmentSpirv)
                    => new GraphicsShaderProgram(
                        new ShaderArtifact(vertexSpirv, "main", VertexAbi),
                        new ShaderArtifact(fragmentSpirv, "main", FragmentAbi));

                public static IGraphicsShaderProgram CreateProgram()
                    => CreateProgram(Sidecar.VertexSpirv, Sidecar.FragmentSpirv);
            }
            {{abiProjection}}
            {{facadeProjection}}
            """;
    }

    public static string ComputeFacadeProjection(IMethodSymbol method, string generatedClassName)
    {
        var container = Identifier(method.ContainingType.Name);
        var generatedType = QualifiedType(method, generatedClassName);
        var entryPoint = Identifier(method.Name);
        return $$"""
            public static partial class Shaders
            {
                public static partial class Abi
                {
                    public static partial class {{container}}
                    {
                        public static ShaderAbi {{entryPoint}}() => {{generatedType}}.Abi;
                    }
                }

                public static partial class Spv
                {
                    public static partial class {{container}}
                    {
                        public static ReadOnlySpan<byte> {{entryPoint}}() => {{generatedType}}.GetSpirv();
                    }
                }
            }
            """;
    }

    public static string GraphicsFacadeProjection(
        IMethodSymbol method,
        string generatedClassName,
        string propertyPrefix)
    {
        var container = Identifier(method.ContainingType.Name);
        var generatedType = QualifiedType(method, generatedClassName);
        var pairType = propertyPrefix.Length == 0 ? string.Empty : Pascalize(propertyPrefix);
        var abiMembers = $$"""
                        public static ShaderAbi Vertex() => {{generatedType}}.VertexAbi;
                        public static ShaderAbi Fragment() => {{generatedType}}.FragmentAbi;
            """;
        var spvMembers = $$"""
                        public static ReadOnlySpan<byte> Vertex() => {{generatedType}}.GetVertexSpirv();
                        public static ReadOnlySpan<byte> Fragment() => {{generatedType}}.GetFragmentSpirv();
            """;

        if (pairType.Length == 0)
        {
            return $$"""
                public static partial class Shaders
                {
                    public static partial class Abi
                    {
                        public static partial class {{container}}
                        {
                {{abiMembers}}
                        }
                    }

                    public static partial class Spv
                    {
                        public static partial class {{container}}
                        {
                {{spvMembers}}
                        }
                    }
                }
                """;
        }

        return $$"""
            public static partial class Shaders
            {
                public static partial class Abi
                {
                    public static partial class {{container}}
                    {
                        public static partial class {{pairType}}
                        {
                {{abiMembers}}
                        }
                    }
                }

                public static partial class Spv
                {
                    public static partial class {{container}}
                    {
                        public static partial class {{pairType}}
                        {
                {{spvMembers}}
                        }
                    }
                }
            }
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

    private static string QualifiedType(IMethodSymbol method, string className)
        => method.ContainingNamespace.IsGlobalNamespace
            ? $"global::{className}"
            : $"global::{method.ContainingNamespace.ToDisplayString()}.{className}";

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string Identifier(string value)
        => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } identifier
            ? identifier
            : string.Empty;

    private static string Pascalize(string value)
    {
        var result = new StringBuilder();
        var capitalize = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                capitalize = true;
                continue;
            }

            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        return result.Length == 0 ? "Graphics" : result.ToString();
    }
}
