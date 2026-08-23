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
        var vertices = methods.Where(m => m!.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName)).ToArray();
        var fragments = methods.Where(m => m!.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName)).ToArray();
        if (vertices.Length != 1 || fragments.Length != 1) { context.ReportDiagnostic(Diagnostic.Create(Descriptor, methods[0]!.Locations.FirstOrDefault(), "DSH017: exactly one vertex and one fragment shader are required for a generated graphics program.")); return; }
        var results = ShaderCompiler.CompileAll(compilation).Where(r => r.Module?.Stage is ShaderStage.Vertex or ShaderStage.Fragment).ToArray();
        if (results.Length != 2 || results.Any(r => !r.Success || r.AbiManifest is null || r.Module is null))
        {
            foreach (var d in results.SelectMany(r => r.Diagnostics))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, methods[0]!.Locations.FirstOrDefault(), $"{d.Id}: {d.Message}"));
            }

            return;
        }
        var emitted = results.Select(r => (r, GlslEmitter.EmitFromModule(r.Module!))).ToArray();
        if (emitted.Any(x => !x.Item2.Success)) { context.ReportDiagnostic(Diagnostic.Create(Descriptor, methods[0]!.Locations.FirstOrDefault(), "GLSL generation failed for the graphics shader pair.")); return; }
        var vertex = results.Single(r => r.Module!.Stage == ShaderStage.Vertex);
        var fragment = results.Single(r => r.Module!.Stage == ShaderStage.Fragment);
        var vertexGlsl = emitted.Single(x => x.r.Module!.Stage == ShaderStage.Vertex).Item2.Source;
        var fragmentGlsl = emitted.Single(x => x.r.Module!.Stage == ShaderStage.Fragment).Item2.Source;
        var type = vertices[0]!.ContainingType;
        var name = Sanitize(type.Name) + "GraphicsShaderProgram";
        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : $"namespace {type.ContainingNamespace.ToDisplayString()};";
        var source = "using System;\nusing System.Text.Json;\nusing Delta.Shader.Abstractions;\n\n" + ns + "\n\npublic static class " + name + "\n{\n" +
            "    public const string VertexGlsl = " + Literal(vertexGlsl) + ";\n    public const string FragmentGlsl = " + Literal(fragmentGlsl) + ";\n    public const string VertexManifestJson = " + Literal(JsonSerializer.Serialize(vertex.AbiManifest)) + ";\n    public const string FragmentManifestJson = " + Literal(JsonSerializer.Serialize(fragment.AbiManifest)) + ";\n\n" +
            "    public static GraphicsShaderProgram CreateProgram(byte[] vertexSpirv, byte[] fragmentSpirv)\n    {\n        var v = JsonSerializer.Deserialize<ShaderAbiManifest>(VertexManifestJson);\n        var f = JsonSerializer.Deserialize<ShaderAbiManifest>(FragmentManifestJson);\n        if (v is null || f is null) throw new InvalidOperationException(\"Generated graphics manifests could not be deserialized.\");\n        return new GraphicsShaderProgram(new ShaderArtifact(vertexSpirv, v), new ShaderArtifact(fragmentSpirv, f));\n    }\n}\n";
        context.AddSource(name + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
    private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } value ? value : "Graphics";
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
