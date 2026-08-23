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

        var firstMethod = methodsInAssembly[0];
        var vertices = methodsInAssembly.Where(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName)).ToArray();
        var fragments = methodsInAssembly.Where(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName)).ToArray();
        if (vertices.Length != 1 || fragments.Length != 1) { context.ReportDiagnostic(Diagnostic.Create(Descriptor, firstMethod.Locations.FirstOrDefault(), "DSH017: exactly one vertex and one fragment shader are required for a generated graphics program.")); return; }
        var results = ShaderCompiler.CompileAll(compilation).Where(r => r.Module?.Stage is ShaderStage.Vertex or ShaderStage.Fragment).ToArray();
        if (results.Length != 2 || results.Any(r => !r.Success || r.AbiManifest is null || r.Module is null))
        {
            foreach (var d in results.SelectMany(r => r.Diagnostics))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, firstMethod.Locations.FirstOrDefault(), $"{d.Id}: {d.Message}"));
            }

            return;
        }
        var vertexResult = results.Single(result => result.Module?.Stage == ShaderStage.Vertex);
        var fragmentResult = results.Single(result => result.Module?.Stage == ShaderStage.Fragment);
        var vertexModule = vertexResult.Module ?? throw new InvalidOperationException("Vertex shader module is missing after successful compilation.");
        var fragmentModule = fragmentResult.Module ?? throw new InvalidOperationException("Fragment shader module is missing after successful compilation.");
        var vertexEmit = GlslEmitter.EmitFromModule(vertexModule);
        var fragmentEmit = GlslEmitter.EmitFromModule(fragmentModule);
        if (!vertexEmit.Success || !fragmentEmit.Success) { context.ReportDiagnostic(Diagnostic.Create(Descriptor, firstMethod.Locations.FirstOrDefault(), "GLSL generation failed for the graphics shader pair.")); return; }
        var vertexManifest = vertexResult.AbiManifest ?? throw new InvalidOperationException("Vertex shader manifest is missing after successful compilation.");
        var fragmentManifest = fragmentResult.AbiManifest ?? throw new InvalidOperationException("Fragment shader manifest is missing after successful compilation.");
        var vertexGlsl = vertexEmit.Source;
        var fragmentGlsl = fragmentEmit.Source;
        var type = vertices[0].ContainingType;
        var name = Sanitize(type.Name) + "GraphicsShaderProgram";
        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : $"namespace {type.ContainingNamespace.ToDisplayString()};";
        var source = "using System;\nusing System.Text.Json;\nusing Delta.Shader.Abstractions;\n\n" + ns + "\n\npublic static class " + name + "\n{\n" +
            "    public const string VertexGlsl = " + Literal(vertexGlsl) + ";\n    public const string FragmentGlsl = " + Literal(fragmentGlsl) + ";\n    public const string VertexManifestJson = " + Literal(JsonSerializer.Serialize(vertexManifest)) + ";\n    public const string FragmentManifestJson = " + Literal(JsonSerializer.Serialize(fragmentManifest)) + ";\n\n" +
            "    public static GraphicsShaderProgram CreateProgram(byte[] vertexSpirv, byte[] fragmentSpirv)\n    {\n        var v = JsonSerializer.Deserialize<ShaderAbiManifest>(VertexManifestJson);\n        var f = JsonSerializer.Deserialize<ShaderAbiManifest>(FragmentManifestJson);\n        if (v is null || f is null) throw new InvalidOperationException(\"Generated graphics manifests could not be deserialized.\");\n        return new GraphicsShaderProgram(new ShaderArtifact(vertexSpirv, v), new ShaderArtifact(fragmentSpirv, f));\n    }\n}\n";
        context.AddSource(name + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
    private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } value ? value : "Graphics";
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
