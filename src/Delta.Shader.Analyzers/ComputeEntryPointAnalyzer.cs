using System.Collections.Immutable;
using System.Linq;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace Delta.Shader.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComputeEntryPointAnalyzer : DiagnosticAnalyzer
{
    public const string DescriptorId = ShaderDiagnosticId.DSH004;
    private const string VisibleTypeDescriptorId = ShaderDiagnosticId.DSH010;
    private const string GraphicsDescriptorId = ShaderDiagnosticId.DSH012;

    private static readonly DiagnosticDescriptor _descriptor = new(
        id: DescriptorId,
        title: "Invalid compute entry point",
        messageFormat: "Compute entry point must be static and return void",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _graphicsDescriptor = new(
        id: GraphicsDescriptorId,
        title: "Invalid graphics shader entry point",
        messageFormat: "Graphics shader entry point must be static and return void",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _visibleTypeDescriptor = new(
        id: VisibleTypeDescriptorId,
        title: "Invalid shader-visible type",
        messageFormat: "{0}",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [_descriptor, _graphicsDescriptor, _visibleTypeDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        var computeShaderAttribute = typeof(ComputeShaderAttribute).FullName;
        context.RegisterSymbolAction(context =>
        {
            if (context.Compilation.GetTypeByMetadataName(computeShaderAttribute) is not ITypeSymbol attributeType)
            {
                return;
            }

            if (context.Symbol is not IMethodSymbol methodSymbol)
            {
                return;
            }

            var attribute = methodSymbol.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));

            var vertexAttribute = context.Compilation.GetTypeByMetadataName(typeof(VertexShaderAttribute).FullName);
            var fragmentAttribute = context.Compilation.GetTypeByMetadataName(typeof(FragmentShaderAttribute).FullName);
            var graphicsAttribute = methodSymbol.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, vertexAttribute) ||
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, fragmentAttribute));

            if (attribute is null && graphicsAttribute is null)
            {
                return;
            }

            foreach (var parameter in methodSymbol.Parameters)
            {
                var visibleType = ShaderVisibleTypeValidation.GetVisibleRootType(parameter, context.Compilation);
                foreach (var issue in ShaderVisibleTypeValidation.Validate(visibleType, parameter))
                {
                    var location = issue.Symbol.Locations.FirstOrDefault() ?? parameter.Locations[0];
                    context.ReportDiagnostic(Diagnostic.Create(_visibleTypeDescriptor, location, issue.Message));
                }
            }

            if (!methodSymbol.IsStatic || methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
            {
                context.ReportDiagnostic(Diagnostic.Create(graphicsAttribute is null ? _descriptor : _graphicsDescriptor, methodSymbol.Locations[0]));
            }
        }, SymbolKind.Method);
    }
}
