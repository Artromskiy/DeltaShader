using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Delta.Shader;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Delta.Shader.Analyzers;

[Generator]
public sealed class DeltaComputeGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Descriptor = new(
        ShaderDiagnosticId.DSH016,
        "Compile-time shader generation failed",
        "{0}",
        "DeltaShader",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                typeof(ComputeShaderAttribute).FullName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, _) => attributeContext.TargetSymbol as IMethodSymbol)
            .Where(static method => method is not null).Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(methods),
            static (sourceContext, input) => Execute(input.Left, input.Right, sourceContext));
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
        if (!result.Success || result.Module is null || result.BuildManifest is null)
        {
            ReportDiagnostics(context, method, result);
            return;
        }

        var emitted = GlslEmitter.EmitFromModule(result.Module);
        if (!emitted.Success)
        {
            ReportDiagnostic(context, method, "GLSL generation failed for the [ComputeShader] method.");
            return;
        }

        if (!ArtifactSourceEmitter.TryEmitPackingMethods(method, result.BuildManifest, out var packingMethods, out var packingReason))
        {
            ReportDiagnostic(context, method, $"Std430 packer generation failed: {packingReason}");
            return;
        }

        var className = Sanitize(method.ContainingType.Name) + Sanitize(method.Name) + "ShaderArtifact";
        var source = BuildArtifactSource(
            method,
            className,
            ArtifactSourceEmitter.EmitAbiFactory(result.BuildManifest),
            packingMethods);
        context.AddSource(className + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void ReportDiagnostics(
        SourceProductionContext context,
        IMethodSymbol method,
        ShaderCompilationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            ReportDiagnostic(context, method, $"{diagnostic.Id}: {diagnostic.Message}");
        }
    }

    private static void ReportDiagnostic(
        SourceProductionContext context,
        IMethodSymbol method,
        string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            method.Locations.FirstOrDefault(),
            message));
    }

    private static string BuildArtifactSource(IMethodSymbol method, string className, string abiFactory, string packingMethods)
    {
        var ns = method.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : $"namespace {method.ContainingNamespace.ToDisplayString()};";

        return "using System;\nusing Delta.Shader.Contract;\n\n" + ns + "\n\npublic static class " + className + "\n{\n" +
            abiFactory +
            ArtifactSourceEmitter.EmitAbiAccessor("Abi", "CreateAbi") +
            packingMethods +
            "\n    public static ShaderArtifact CreateArtifact(ReadOnlySpan<byte> spirv)\n        => new(spirv, \"main\", Abi);\n}\n";
    }

    private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) is { Length: > 0 } value ? value : "Compute";
}
