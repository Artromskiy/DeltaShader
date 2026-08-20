using System;
using System.Collections.Immutable;
using System.Linq;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace Delta.Shader.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComputeEntryPointAnalyzer : DiagnosticAnalyzer
{
    public const string DescriptorId = ShaderDiagnosticId.DSH004;
    private const string VisibleTypeDescriptorId = ShaderDiagnosticId.DSH010;
    private const string GraphicsDescriptorId = ShaderDiagnosticId.DSH012;
    private const string UnsupportedConstructDescriptorId = ShaderDiagnosticId.DSH014;

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

    private static readonly DiagnosticDescriptor _unsupportedConstructDescriptor = new(
        id: UnsupportedConstructDescriptorId,
        title: "Unsupported compile-time shader construct",
        messageFormat: "{0}",
        category: "Delta.Shader",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [_descriptor, _graphicsDescriptor, _visibleTypeDescriptor, _unsupportedConstructDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        var computeShaderAttribute = typeof(ComputeShaderAttribute).FullName;
        var deltaComputeAttribute = typeof(DeltaComputeAttribute).FullName;
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
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType) ||
                    a.AttributeClass?.ToDisplayString() == deltaComputeAttribute);

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

        context.RegisterSyntaxNodeAction(AnalyzeCompileTimeBody, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeCompileTimeBody(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax syntax ||
            context.SemanticModel.GetDeclaredSymbol(syntax) is not IMethodSymbol method ||
            !method.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == typeof(ComputeShaderAttribute).FullName ||
                attribute.AttributeClass?.ToDisplayString() == typeof(DeltaComputeAttribute).FullName))
        {
            return;
        }

        var model = context.SemanticModel;
        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
            {
                continue;
            }

            var namespaceName = called.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (namespaceName.StartsWith("System.Reflection", StringComparison.Ordinal))
            {
                Report(context.ReportDiagnostic, invocation.GetLocation(),
                    "Reflection calls are not allowed in compile-time shaders.");
            }
            else if ((called.IsVirtual || called.IsAbstract || called.IsOverride) &&
                     !namespaceName.StartsWith("Delta.Shader.Abstractions", StringComparison.Ordinal) &&
                     !namespaceName.StartsWith("Delta.Maths", StringComparison.Ordinal))
            {
                Report(context.ReportDiagnostic, invocation.GetLocation(),
                    "Virtual and interface calls are not allowed in compile-time shaders.");
            }
        }

        foreach (var identifier in syntax.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier).Symbol is not IFieldSymbol field)
            {
                continue;
            }

            var namespaceName = field.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!field.IsConst &&
                !namespaceName.StartsWith("Delta.Shader.Abstractions", StringComparison.Ordinal) &&
                !namespaceName.StartsWith("Delta.Maths", StringComparison.Ordinal))
            {
                Report(context.ReportDiagnostic, identifier.GetLocation(),
                    "Managed mutable state is not allowed in compile-time shaders.");
            }
        }

        foreach (var declaration in syntax.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            foreach (var variable in declaration.Variables)
            {
                var type = model.GetTypeInfo(variable.Initializer?.Value ?? declaration.Type).Type;
                if (type is null || IsShaderValueType(type))
                {
                    continue;
                }

                Report(context.ReportDiagnostic, variable.GetLocation(),
                    $"Reference local '{type.ToDisplayString()}' is not allowed in compile-time shaders.");
            }
        }
    }

    private static bool IsShaderValueType(ITypeSymbol type)
        => type.IsValueType ||
           type.SpecialType is SpecialType.System_UInt32 or SpecialType.System_Int32 or SpecialType.System_Single or SpecialType.System_Boolean;

    private static void Report(Action<Diagnostic> reportDiagnostic, Location location, string message)
        => reportDiagnostic(Diagnostic.Create(_unsupportedConstructDescriptor, location, message));
}
