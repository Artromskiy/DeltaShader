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
public sealed class DeltaGraphicsGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Descriptor = new(ShaderDiagnosticId.DSH019, "Compile-time graphics generation failed", "{0}", "DeltaShader", DiagnosticSeverity.Error, true);
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax,
                static (syntaxContext, _) => GetShaderMethod(syntaxContext))
            .Where(static method => method is not null)
            .Collect();

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
        var methodsInAssembly = methods.OfType<IMethodSymbol>().ToArray();
        if (methodsInAssembly.Length == 0)
        {
            return;
        }

        var vertices = methodsInAssembly.Where(IsVertexShader).ToArray();
        var fragments = methodsInAssembly.Where(IsFragmentShader).ToArray();
        var singlePair = vertices.Length == 1 && fragments.Length == 1;
        var sharedVertex = vertices.Length == 1 && fragments.Length > 1;
        var pairNames = singlePair
            ? ["__single_graphics_pair"]
            : sharedVertex
                ? fragments.Select(GetShaderName).Distinct(StringComparer.Ordinal).ToArray()
                : vertices.Select(GetShaderName).Concat(fragments.Select(GetShaderName)).Distinct(StringComparer.Ordinal).ToArray();
        var allResults = ShaderCompiler.CompileAll(compilation).ToArray();
        if (allResults.Any(r => !r.Success || r.BuildManifest is null || r.Module is null))
        {
            foreach (var d in allResults.SelectMany(r => r.Diagnostics))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, methodsInAssembly[0].Locations.FirstOrDefault(), $"{d.Id}: {d.Message}"));
            }

            return;
        }

        var results = allResults.Where(r => r.Module?.Stage is ShaderStage.Vertex or ShaderStage.Fragment).ToArray();
        foreach (var pairName in pairNames)
        {
            var pairVertices = singlePair || sharedVertex ? vertices : vertices.Where(method => GetShaderName(method) == pairName).ToArray();
            var pairFragments = singlePair ? fragments : fragments.Where(method => GetShaderName(method) == pairName).ToArray();
            if (pairVertices.Length != 1 || pairFragments.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, methodsInAssembly[0].Locations.FirstOrDefault(), $"DSH017: graphics pair '{pairName}' requires exactly one vertex and one fragment shader."));
                continue;
            }

            var vertexResult = FindResult(results, ShaderStage.Vertex, pairName, sharedVertex, singlePair);
            var fragmentResult = FindResult(results, ShaderStage.Fragment, pairName, false, singlePair);
            if (vertexResult?.Module is null || vertexResult.BuildManifest is null || fragmentResult?.Module is null || fragmentResult.BuildManifest is null)
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

            var vertexPackingSucceeded = ArtifactSourceEmitter.TryEmitPackingMethods(
                pairVertices[0], vertexResult.BuildManifest, out var vertexPacking, out var vertexPackingReason);
            var fragmentPackingSucceeded = ArtifactSourceEmitter.TryEmitPackingMethods(
                pairFragments[0], fragmentResult.BuildManifest, out var fragmentPacking, out var fragmentPackingReason);
            if (!vertexPackingSucceeded || !fragmentPackingSucceeded)
            {
                var reason = vertexPackingReason ?? fragmentPackingReason ?? "unknown packing error";
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, pairVertices[0].Locations.FirstOrDefault(), $"Std430 packer generation failed for graphics pair '{pairName}': {reason}"));
                continue;
            }

            var type = pairVertices[0].ContainingType;
            var name = pairNames.Length == 1 ? Sanitize(type.Name) + "GraphicsShaderProgram" : Pascalize(pairName) + "GraphicsShaderProgram";
            var source = GeneratedArtifactSource.Graphics(
                pairVertices[0],
                name,
                ArtifactSourceEmitter.EmitAbiFactory(vertexResult.BuildManifest),
                ArtifactSourceEmitter.EmitAbiFactory(fragmentResult.BuildManifest, "CreateFragmentAbi"),
                ArtifactSourceEmitter.EmitAbiAccessor("VertexAbi", "CreateAbi"),
                ArtifactSourceEmitter.EmitAbiAccessor("FragmentAbi", "CreateFragmentAbi"),
                vertexPacking,
                fragmentPacking,
                pairVertices[0].Name + ".vert.spv",
                pairFragments[0].Name + ".frag.spv",
                GeneratedArtifactSource.GraphicsAbiProjection(
                    pairVertices[0],
                    name,
                    pairNames.Length == 1 ? string.Empty : pairName),
                GeneratedArtifactSource.GraphicsFacadeProjection(
                    pairVertices[0],
                    name,
                    pairNames.Length == 1 ? string.Empty : pairName));
            context.AddSource(name + ".g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }
    private static IMethodSymbol? GetShaderMethod(GeneratorSyntaxContext syntaxContext)
    {
        var method = syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node) as IMethodSymbol;
        return method is not null && IsShaderMethod(method) ? method : null;
    }

    private static bool IsShaderMethod(IMethodSymbol method)
        => IsVertexShader(method) || IsFragmentShader(method);

    private static bool IsVertexShader(IMethodSymbol method)
        => HasAttribute(method, typeof(VertexShaderAttribute));

    private static bool IsFragmentShader(IMethodSymbol method)
        => HasAttribute(method, typeof(FragmentShaderAttribute));

    private static bool HasAttribute(IMethodSymbol method, Type attributeType)
        => method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == attributeType.FullName);

    private static string GetShaderName(IMethodSymbol method)
    {
        var attribute = method.GetAttributes().First(attribute =>
            attribute.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName ||
            attribute.AttributeClass?.ToDisplayString() == typeof(FragmentShaderAttribute).FullName);
        return attribute.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? method.Name;
    }
    private static ShaderCompilationResult? FindResult(
        ShaderCompilationResult[] results,
        ShaderStage stage,
        string pairName,
        bool sharedVertex,
        bool singlePair)
    {
        var matches = results.Where(result => result.Module?.Stage == stage &&
            (singlePair ||
             (sharedVertex && stage == ShaderStage.Vertex) ||
             result.Module.SourceEntryPointName == pairName)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
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
}
