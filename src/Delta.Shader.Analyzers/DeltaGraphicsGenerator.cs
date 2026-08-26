using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using Delta.Shader.Abstractions;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Delta.Shader.Analyzers;

[Generator]
public sealed class DeltaGraphicsGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Descriptor = new(ShaderDiagnosticId.DSH019, "Compile-time graphics generation failed", "{0}", "Delta.Shader", DiagnosticSeverity.Error, true);
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.CreateSyntaxProvider(static (node, _) => node is MethodDeclarationSyntax,
            static (syntaxContext, _) =>
            {
                var method = syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node) as IMethodSymbol;
                return method is not null && method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName || a.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName) ? method : null;
            }).Where(static method => method is not null).Collect();
        context.RegisterSourceOutput(context.CompilationProvider.Combine(methods), static (sourceContext, input) => Execute(input.Left, input.Right, sourceContext));
    }
    private static void Execute(Compilation compilation, ImmutableArray<IMethodSymbol?> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty)
        {
            return;
        }
        var methodsInAssembly = methods.OfType<IMethodSymbol>().ToArray();
        if (methodsInAssembly.Length == 0)
        {
            return;
        }

        var vertices = methodsInAssembly.Where(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName)).ToArray();
        var fragments = methodsInAssembly.Where(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName)).ToArray();
        var singlePair = vertices.Length == 1 && fragments.Length == 1;
        var pairNames = singlePair
            ? ["__single_graphics_pair"]
            : vertices.Select(GetShaderName).Concat(fragments.Select(GetShaderName)).Distinct(StringComparer.Ordinal).ToArray();
        var results = ShaderCompiler.CompileAll(compilation).Where(r => r.Module?.Stage is ShaderStage.Vertex or ShaderStage.Fragment).ToArray();
        if (results.Any(r => !r.Success || r.AbiManifest is null || r.Module is null))
        {
            foreach (var d in results.SelectMany(r => r.Diagnostics))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, methodsInAssembly[0].Locations.FirstOrDefault(), $"{d.Id}: {d.Message}"));
            }

            return;
        }
        foreach (var pairName in pairNames)
        {
            var pairVertices = singlePair ? vertices : vertices.Where(method => GetShaderName(method) == pairName).ToArray();
            var pairFragments = singlePair ? fragments : fragments.Where(method => GetShaderName(method) == pairName).ToArray();
            if (pairVertices.Length != 1 || pairFragments.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, methodsInAssembly[0].Locations.FirstOrDefault(), $"DSH017: graphics pair '{pairName}' requires exactly one vertex and one fragment shader."));
                continue;
            }

            var resultsByIdentity = results.GroupBy(result => result.SourceMethodIdentity)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var vertexIdentity = pairVertices[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fragmentIdentity = pairFragments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var vertexResult = resultsByIdentity.TryGetValue(vertexIdentity, out var vertexMatches) && vertexMatches.Length == 1 ? vertexMatches[0] : null;
            var fragmentResult = resultsByIdentity.TryGetValue(fragmentIdentity, out var fragmentMatches) && fragmentMatches.Length == 1 ? fragmentMatches[0] : null;
            if (vertexResult?.Module is null || vertexResult.AbiManifest is null || fragmentResult?.Module is null || fragmentResult.AbiManifest is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, pairVertices[0].Locations.FirstOrDefault(), $"DSH017: graphics pair '{pairName}' did not produce both shader modules."));
                continue;
            }

            var vertexEmit = GlslEmitter.EmitFromModule(vertexResult.Module);
            var fragmentEmit = GlslEmitter.EmitFromModule(fragmentResult.Module);
            if (!vertexEmit.Success || !fragmentEmit.Success)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, pairVertices[0].Locations.FirstOrDefault(), $"GLSL generation failed for graphics pair '{pairName}'."));
                continue;
            }

            var type = pairVertices[0].ContainingType;
            var name = pairNames.Length == 1 ? Sanitize(type.Name) + "GraphicsShaderProgram" : Pascalize(pairName) + "GraphicsShaderProgram";
            var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : $"namespace {type.ContainingNamespace.ToDisplayString()};";
            var source = "using System;\nusing System.Text.Json;\nusing Delta.Shader.Contract;\n\n" + ns + "\n\npublic static class " + name + "\n{\n" +
                "    public const string VertexGlsl = " + Literal(vertexEmit.Source) + ";\n    public const string FragmentGlsl = " + Literal(fragmentEmit.Source) + ";\n    public const string VertexManifestJson = " + Literal(JsonSerializer.Serialize(vertexResult.AbiManifest)) + ";\n    public const string FragmentManifestJson = " + Literal(JsonSerializer.Serialize(fragmentResult.AbiManifest)) + ";\n\n" +
                ArtifactSourceEmitter.EmitAbiFactory(vertexResult.AbiManifest) +
                ArtifactSourceEmitter.EmitAbiFactory(fragmentResult.AbiManifest).Replace("CreateAbi", "CreateFragmentAbi") +
                "\n    public static IGraphicsShaderProgram CreateProgram(ReadOnlySpan<byte> vertexSpirv, ReadOnlySpan<byte> fragmentSpirv)\n        => new GraphicsShaderProgram(new ShaderArtifact(vertexSpirv, \"main\", CreateAbi()), new ShaderArtifact(fragmentSpirv, \"main\", CreateFragmentAbi()));\n}\n";
            context.AddSource(name + ".g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }
    private static string GetShaderName(IMethodSymbol method)
    {
        var attribute = method.GetAttributes().First(attribute => attribute.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName || attribute.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName);
        return attribute.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? method.Name;
    }
    private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } value ? value : "Graphics";
    private static string Pascalize(string name)
    {
        var result = new StringBuilder();
        var capitalize = true;
        foreach (var character in name)
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
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
