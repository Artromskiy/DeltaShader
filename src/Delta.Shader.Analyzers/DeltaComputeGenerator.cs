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
    private static readonly DiagnosticDescriptor CompilationDescriptor = new(
        id: ShaderDiagnosticId.DSH016,
        title: "Compile-time shader generation failed",
        messageFormat: "{0}",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                typeof(DeltaComputeAttribute).FullName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, _) => attributeContext.TargetSymbol as IMethodSymbol)
            .Where(static method => method is not null)
            .Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(methods),
            static (sourceContext, input) => Execute(input.Left, input.Right, sourceContext));
    }

    private static void Execute(
        Compilation roslynCompilation,
        ImmutableArray<IMethodSymbol?> methods,
        SourceProductionContext sourceContext)
    {
        if (methods.IsDefaultOrEmpty)
        {
            return;
        }

        var compilation = ShaderCompiler.Compile(roslynCompilation);
        if (!compilation.Success || compilation.Module is null || compilation.AbiManifest is null)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                sourceContext.ReportDiagnostic(Diagnostic.Create(
                    CompilationDescriptor,
                    methods[0]!.Locations.FirstOrDefault(),
                    $"{diagnostic.Id}: {diagnostic.Message}"));
            }

            return;
        }

        var emitted = GlslEmitter.EmitFromModule(compilation.Module);
        if (!emitted.Success)
        {
            sourceContext.ReportDiagnostic(Diagnostic.Create(
                CompilationDescriptor,
                methods[0]!.Locations.FirstOrDefault(),
                "GLSL generation failed for the [DeltaCompute] method."));
            return;
        }

        var method = methods[0]!;
        var className = Sanitize(method.ContainingType.Name) + Sanitize(method.Name) + "ShaderArtifact";
        var namespaceName = method.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : $"namespace {method.ContainingNamespace.ToDisplayString()};";
        var manifestJson = JsonSerializer.Serialize(compilation.AbiManifest);
        var source = "using System;\n" +
            "using System.Text.Json;\n" +
            "using Delta.Shader.Abstractions;\n\n" +
            namespaceName + "\n\n" +
            "public static class " + className + "\n{\n" +
            "    public const string SourceEntryPointName = " + Literal(compilation.AbiManifest.SourceEntryPointName) + ";\n" +
            "    public const string EntryPointName = " + Literal(compilation.AbiManifest.EntryPointName) + ";\n" +
            "    public const string Glsl = " + Literal(emitted.Source) + ";\n" +
            "    public const string ManifestJson = " + Literal(manifestJson) + ";\n\n" +
            "    public static ShaderArtifact CreateArtifact(byte[] spirv)\n    {\n" +
            "        var manifest = JsonSerializer.Deserialize<ShaderAbiManifest>(ManifestJson);\n" +
            "        if (manifest is null)\n        {\n" +
            "            throw new InvalidOperationException(\"Generated DeltaCompute manifest could not be deserialized.\");\n        }\n\n" +
            "        return new ShaderArtifact(spirv, manifest);\n    }\n}\n";

        sourceContext.AddSource(className + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.Length == 0 ? "Compute" : builder.ToString();
    }

    private static string Literal(string value)
    {
        const string quote = "\"";
        return quote + value
            .Replace("\\", "\\\\")
            .Replace(quote, "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + quote;
    }
}
