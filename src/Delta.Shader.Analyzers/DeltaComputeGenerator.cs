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
public sealed class DeltaComputeGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Descriptor = new(ShaderDiagnosticId.DSH016, "Compile-time shader generation failed", "{0}", "Delta.Shader", DiagnosticSeverity.Error, true);
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(typeof(DeltaComputeAttribute).FullName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => attributeContext.TargetSymbol as IMethodSymbol)
            .Where(static method => method is not null).Collect();
        context.RegisterSourceOutput(context.CompilationProvider.Combine(methods), static (sourceContext, input) => Execute(input.Left, input.Right, sourceContext));
    }
    private static void Execute(Compilation compilation, ImmutableArray<IMethodSymbol?> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty)
        {
            return;
        }

        var method = methods.FirstOrDefault(static candidate => candidate is not null);
        if (method is null)
        {
            return;
        }

        var result = ShaderCompiler.Compile(compilation);
        if (!result.Success || result.Module is null || result.AbiManifest is null)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Locations.FirstOrDefault(), $"{diagnostic.Id}: {diagnostic.Message}"));
            }
            return;
        }
        var emitted = GlslEmitter.EmitFromModule(result.Module);
        if (!emitted.Success) { context.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Locations.FirstOrDefault(), "GLSL generation failed for the [DeltaCompute] method.")); return; }
        var className = Sanitize(method.ContainingType.Name) + Sanitize(method.Name) + "ShaderArtifact";
        var ns = method.ContainingNamespace.IsGlobalNamespace ? string.Empty : $"namespace {method.ContainingNamespace.ToDisplayString()};";
        var source = "using System;\nusing System.Text.Json;\nusing Delta.Shader.Abstractions;\n\n" + ns + "\n\npublic static class " + className + "\n{\n" +
            "    public const string SourceEntryPointName = " + Literal(result.AbiManifest.SourceEntryPointName) + ";\n    public const string EntryPointName = " + Literal(result.AbiManifest.EntryPointName) + ";\n    public const string Glsl = " + Literal(emitted.Source) + ";\n    public const string ManifestJson = " + Literal(JsonSerializer.Serialize(result.AbiManifest)) + ";\n\n" +
            "    public static ShaderArtifact CreateArtifact(byte[] spirv)\n    {\n        var manifest = JsonSerializer.Deserialize<ShaderAbiManifest>(ManifestJson);\n        if (manifest is null) throw new InvalidOperationException(\"Generated DeltaCompute manifest could not be deserialized.\");\n        return new ShaderArtifact(spirv, manifest);\n    }\n}\n";
        context.AddSource(className + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
    private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } value ? value : "Compute";
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
