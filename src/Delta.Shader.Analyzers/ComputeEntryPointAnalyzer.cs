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

    private static readonly DiagnosticDescriptor _descriptor = new(
        id: DescriptorId,
        title: "Invalid compute entry point",
        messageFormat: "Compute entry point must be static and return void",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_descriptor];

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

            if (attribute is null)
            {
                return;
            }

            if (!methodSymbol.IsStatic || methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
            {
                context.ReportDiagnostic(Diagnostic.Create(_descriptor, methodSymbol.Locations[0]));
            }
        }, SymbolKind.Method);
    }
}
