using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Delta.Shader;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

internal static class ShaderBodyTranslator
{
    public static bool TryTranslateCompute(
        MethodDeclarationSyntax methodSyntax,
        SemanticModel model,
        ModuleCompilationContext context,
        IParameterSymbol? contextParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        IReadOnlyDictionary<IMethodSymbol, string> helperNames,
        IReadOnlyDictionary<INamedTypeSymbol, string> structNames,
        IReadOnlyDictionary<IFieldSymbol, string> structFields,
        IReadOnlyDictionary<IPropertySymbol, string> structProperties,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        IReadOnlyDictionary<IParameterSymbol, string>? parameterMap = null,
        IReadOnlyCollection<IParameterSymbol>? outputParameters = null,
        bool allowValueReturn = false,
        string? instanceReceiver = null,
        IReadOnlyDictionary<IMethodSymbol, bool>? helperReceivers = null)
    {
        var rewriter = CreateComputeRewriter(
            model,
            context,
            contextParameter,
            resourceBindings,
            parameterMap ?? new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default),
            new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default),
            helperNames,
            structNames,
            structFields,
            structProperties,
            outputParameters,
            allowValueReturn,
            instanceReceiver,
            helperReceivers);
        return TryTranslateBody(
            methodSyntax.Body ?? (SyntaxNode?)methodSyntax.ExpressionBody?.Expression,
            rewriter,
            normalizeComputeText: true,
            normalizeWhitespace: false,
            lowerReturns: false,
            applyGraphicsPostProcessing: false,
            out translated,
            out usesBuiltin,
            out reason);
    }

    private static bool ContainsTopLevelGoto(BlockSyntax block)
        => block.Statements.Any(statement =>
            statement is GotoStatementSyntax or LabeledStatementSyntax ||
            statement.DescendantNodes().OfType<GotoStatementSyntax>().Any() ||
            statement.DescendantNodes().OfType<LabeledStatementSyntax>().Any());

    private static bool TryTranslateGotoBlock(BlockSyntax block, Rewriter rewriter, out string translated)
    {
        if (!rewriter.PrepareGotoLocals(block))
        {
            translated = string.Empty;
            return false;
        }

        var statements = block.Statements;
        var labels = statements
            .OfType<LabeledStatementSyntax>()
            .Select((statement, index) => (Label: statement.Identifier.ValueText, State: index + 1))
            .ToDictionary(item => item.Label, item => item.State, StringComparer.Ordinal);
        var segments = new List<(int State, string? Label, List<StatementSyntax> Statements)>();
        var current = (State: 0, Label: (string?)null, Statements: new List<StatementSyntax>());
        foreach (var statement in statements)
        {
            if (statement is LabeledStatementSyntax labeled)
            {
                segments.Add(current);
                var state = labels[labeled.Identifier.ValueText];
                current = (state, labeled.Identifier.ValueText, new List<StatementSyntax>());
                if (labeled.Statement is not EmptyStatementSyntax)
                {
                    current.Statements.Add(labeled.Statement);
                }

                continue;
            }

            current.Statements.Add(statement);
        }

        segments.Add(current);
        foreach (var segment in segments)
        {
            foreach (var statement in segment.Statements)
            {
                if (statement.DescendantNodesAndSelf().OfType<LabeledStatementSyntax>().Any())
                {
                    rewriter.SetReason("Labels inside nested shader statements are not supported.");
                    translated = string.Empty;
                    return false;
                }

                if (statement.DescendantNodesAndSelf().OfType<GotoStatementSyntax>().Any(gotoStatement =>
                    gotoStatement.CaseOrDefaultKeyword.RawKind != 0 ||
                    gotoStatement.Expression is not IdentifierNameSyntax ||
                    gotoStatement.Ancestors().Any(ancestor =>
                        ancestor is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                        ForEachStatementSyntax or SwitchStatementSyntax)))
                {
                    rewriter.SetReason("Shader goto supports labels and if/else control flow, but not goto case/default or transitions from loops and switch sections.");
                    translated = string.Empty;
                    return false;
                }
            }
        }

        var cases = new List<string>(segments.Count);
        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var body = new List<string>();
            foreach (var statement in segment.Statements)
            {
                if (!TryTranslateGotoStatement(
                        statement,
                        rewriter,
                        labels,
                        segment.State,
                        out var statementText))
                {
                    translated = string.Empty;
                    return false;
                }

                body.Add($"if (__delta_goto_state == {segment.State}) {{ {statementText} }}");
            }

            if (body.Count == 0 && segmentIndex + 1 < segments.Count)
            {
                body.Add($"__delta_goto_state = {segments[segmentIndex + 1].State};");
            }

            body.Add($"if (__delta_goto_state == {segment.State}) {{ __delta_goto_state = -1; }}");
            body.Add("break;");
            cases.Add($"case {segment.State}: {{ {string.Join(" ", body)} }}");
        }

        var stateMachine = "int __delta_goto_state = 0; while (__delta_goto_state >= 0) { " +
            string.Join(" ", cases) + " }";
        translated = rewriter.GotoDeclarations.Count == 0
            ? stateMachine
            : string.Join("\n", rewriter.GotoDeclarations) + "\n" + stateMachine;
        return true;
    }

    private static bool TryTranslateGotoStatement(
        StatementSyntax statement,
        Rewriter rewriter,
        IReadOnlyDictionary<string, int> labels,
        int state,
        out string translated)
    {
        if (statement is GotoStatementSyntax gotoStatement)
        {
            var target = gotoStatement.Expression?.ToString();
            if (target is null || !labels.TryGetValue(target, out var targetState))
            {
                rewriter.SetReason($"Shader goto target '{target ?? "<unknown>"}' is not a top-level label.");
                translated = string.Empty;
                return false;
            }

            translated = $"__delta_goto_state = {targetState};";
            return true;
        }

        if (statement is IfStatementSyntax conditional &&
            conditional.Condition is ExpressionSyntax condition &&
            rewriter.Visit(condition) is ExpressionSyntax rewrittenCondition)
        {
            if (!TryTranslateGotoBranch(conditional.Statement, rewriter, labels, state, out var whenTrue))
            {
                translated = string.Empty;
                return false;
            }

            var falseBranch = string.Empty;
            if (conditional.Else is { Statement: { } whenFalse } &&
                !TryTranslateGotoBranch(whenFalse, rewriter, labels, state, out falseBranch))
            {
                translated = string.Empty;
                return false;
            }

            translated = falseBranch.Length == 0
                ? $"if ({rewrittenCondition.ToFullString().Trim()}) {{ {whenTrue} }}"
                : $"if ({rewrittenCondition.ToFullString().Trim()}) {{ {whenTrue} }} else {{ {falseBranch} }}";
            return true;
        }

        if (statement.DescendantNodesAndSelf().OfType<GotoStatementSyntax>().Any())
        {
            rewriter.SetReason("Shader goto is supported in if/else blocks, but this control-flow shape cannot be lowered safely.");
            translated = string.Empty;
            return false;
        }

        if (rewriter.Visit(statement) is not StatementSyntax rewrittenStatement)
        {
            translated = string.Empty;
            return false;
        }

        translated = rewrittenStatement.ToFullString().Trim();
        return true;
    }

    private static bool TryTranslateGotoBranch(
        StatementSyntax statement,
        Rewriter rewriter,
        IReadOnlyDictionary<string, int> labels,
        int state,
        out string translated)
    {
        if (statement is BlockSyntax block)
        {
            var statements = new List<string>(block.Statements.Count);
            foreach (var child in block.Statements)
            {
                if (!TryTranslateGotoStatement(child, rewriter, labels, state, out var childText))
                {
                    translated = string.Empty;
                    return false;
                }

                statements.Add(child is GotoStatementSyntax
                    ? childText
                    : $"if (__delta_goto_state == {state}) {{ {childText} }}");
            }

            translated = string.Join(" ", statements);
            return true;
        }

        return TryTranslateGotoStatement(statement, rewriter, labels, state, out translated);
    }

    public static bool TryTranslateComputeExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        ModuleCompilationContext context,
        IParameterSymbol? contextParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        IReadOnlyDictionary<ILocalSymbol, string> locals,
        IReadOnlyDictionary<IParameterSymbol, string> parameterMap,
        IReadOnlyDictionary<IMethodSymbol, string> helperNames,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        string? instanceReceiver = null,
        IReadOnlyDictionary<INamedTypeSymbol, string>? structNames = null,
        IReadOnlyDictionary<IFieldSymbol, string>? structFields = null,
        IReadOnlyDictionary<IPropertySymbol, string>? structProperties = null,
        IReadOnlyDictionary<IMethodSymbol, bool>? helperReceivers = null,
        IReadOnlyCollection<IParameterSymbol>? outputParameters = null)
    {
        var rewriter = CreateComputeRewriter(model, context, contextParameter, resourceBindings,
            parameterMap, locals, helperNames, structNames, structFields, structProperties,
            outputParameters: outputParameters,
            instanceReceiver: instanceReceiver,
            helperReceivers: helperReceivers);
        return TryTranslateBody(
            expression,
            rewriter,
            normalizeComputeText: true,
            normalizeWhitespace: false,
            lowerReturns: false,
            applyGraphicsPostProcessing: false,
            out translated,
            out usesBuiltin,
            out reason);
    }

    private static Rewriter CreateComputeRewriter(
        SemanticModel model,
        ModuleCompilationContext context,
        IParameterSymbol? contextParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        IReadOnlyDictionary<IParameterSymbol, string> parameterMap,
        IReadOnlyDictionary<ILocalSymbol, string> locals,
        IReadOnlyDictionary<IMethodSymbol, string> helperNames,
        IReadOnlyDictionary<INamedTypeSymbol, string>? structNames = null,
        IReadOnlyDictionary<IFieldSymbol, string>? structFields = null,
        IReadOnlyDictionary<IPropertySymbol, string>? structProperties = null,
        IReadOnlyCollection<IParameterSymbol>? outputParameters = null,
        bool allowValueReturn = false,
        string? instanceReceiver = null,
        IReadOnlyDictionary<IMethodSymbol, bool>? helperReceivers = null)
    {
        var directFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var pushFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var storageFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        if (contextParameter?.Type is INamedTypeSymbol contextType)
        {
            foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
            {
                var attributeNames = field.GetAttributes()
                    .Select(attribute => attribute.AttributeClass?.ToDisplayString())
                    .ToArray();
                if (attributeNames.Contains("Delta.Shader.PushConstantAttribute", StringComparer.Ordinal))
                {
                    pushFields[field] = "pushConstants.member_" + Sanitize(field.Name);
                }
                else if (attributeNames.Contains("Delta.Shader.LayoutAttribute", StringComparer.Ordinal))
                {
                    directFields[field] = field.Name;
                    if (IsStorageBufferType(field.Type, context))
                    {
                        storageFields[field] = field.Name;
                    }
                }
            }
        }

        var parameters = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var parameter in parameterMap)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        var storageParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        foreach (var symbol in resourceBindings.Keys)
        {
            if (symbol is IParameterSymbol parameter)
            {
                if (!parameters.ContainsKey(parameter))
                {
                    parameters[parameter] = parameter.Name;
                }

                storageParameters.Add(parameter);
            }
        }

        return new Rewriter(
            model,
            context,
            ShaderStage.Compute,
            parameters,
            pushFields,
            structNames ?? new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default),
            structFields ?? new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default),
            helperNames,
            directFields,
            new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default),
            null,
            lowerReturns: false,
            locals,
            computeMode: true,
            storageFields,
            storageParameters,
            structProperties,
            outputParameters,
            allowValueReturn,
            instanceReceiver,
            helperReceivers);
    }

    private static bool IsStorageBufferType(ITypeSymbol type, ModuleCompilationContext context)
    {
        var definition = (type as INamedTypeSymbol)?.OriginalDefinition;
        return (context.ReadOnlyStorageBufferType is not null &&
                SymbolEqualityComparer.Default.Equals(definition, context.ReadOnlyStorageBufferType)) ||
               (context.ReadWriteStorageBufferType is not null &&
                SymbolEqualityComparer.Default.Equals(definition, context.ReadWriteStorageBufferType));
    }

    private static string NormalizeComputeText(string text)
        => Regex.Replace(text, @"(?<=\d)f\b", string.Empty);

    public static bool TryTranslate(
        SyntaxNode body,
        SemanticModel model,
        ModuleCompilationContext context,
        ShaderStage stage,
        IReadOnlyDictionary<IParameterSymbol, string> parameterMap,
        IReadOnlyDictionary<IFieldSymbol, string> pushFieldMap,
        IReadOnlyDictionary<INamedTypeSymbol, string> structNames,
        IReadOnlyDictionary<IFieldSymbol, string> structFields,
        IReadOnlyCollection<string> storageBufferTargets,
        IReadOnlyDictionary<IMethodSymbol, string> helperNames,
        out string translated,
        out string? reason,
        IReadOnlyDictionary<IFieldSymbol, string>? directFields = null,
        IReadOnlyDictionary<IFieldSymbol, string>? outputFields = null,
        INamedTypeSymbol? returnType = null,
        bool lowerReturns = false,
        string? instanceReceiver = null,
        IReadOnlyDictionary<IPropertySymbol, string>? structProperties = null,
        IReadOnlyDictionary<IMethodSymbol, bool>? helperReceivers = null,
        IReadOnlyCollection<IParameterSymbol>? outputParameters = null)
    {
        var rewriter = new Rewriter(
            model,
            context,
            stage,
            parameterMap,
            pushFieldMap,
            structNames,
            structFields,
            helperNames,
            directFields ?? new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default),
            outputFields ?? new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default),
            returnType,
            lowerReturns,
            structProperties: structProperties,
            instanceReceiver: instanceReceiver,
            helperReceivers: helperReceivers,
            outputParameters: outputParameters);
        return TryTranslateBody(
            body,
            rewriter,
            normalizeComputeText: false,
            normalizeWhitespace: true,
            lowerReturns: lowerReturns,
            applyGraphicsPostProcessing: true,
            out translated,
            out _,
            out reason,
            model: model,
            context: context,
            stage: stage,
            parameterMap: parameterMap,
            pushFieldMap: pushFieldMap,
            storageBufferTargets: storageBufferTargets);
    }

    private static bool TryTranslateBody(
        SyntaxNode? body,
        Rewriter rewriter,
        bool normalizeComputeText,
        bool normalizeWhitespace,
        bool lowerReturns,
        bool applyGraphicsPostProcessing,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        SemanticModel? model = null,
        ModuleCompilationContext? context = null,
        ShaderStage stage = ShaderStage.Compute,
        IReadOnlyDictionary<IParameterSymbol, string>? parameterMap = null,
        IReadOnlyDictionary<IFieldSymbol, string>? pushFieldMap = null,
        IReadOnlyCollection<string>? storageBufferTargets = null)
    {
        if (body is null)
        {
            translated = string.Empty;
            usesBuiltin = false;
            reason = "Shader methods must have a translatable body.";
            return false;
        }

        if (body is BlockSyntax methodBlock)
        {
            rewriter.PredeclareOutLocals(methodBlock);
        }
        else if (body is ExpressionSyntax expressionBody &&
                 rewriter.ContainsOutLocalDeclaration(expressionBody))
        {
            translated = string.Empty;
            usesBuiltin = false;
            reason = "Expression-bodied shader methods cannot declare local out variables; use a block body.";
            return false;
        }

        var rewritten = body is BlockSyntax gotoBlock && ContainsTopLevelGoto(gotoBlock)
            ? TryTranslateGotoBlock(gotoBlock, rewriter, out var gotoTranslation)
                ? SyntaxFactory.ParseStatement(gotoTranslation)
                : null
            : body is ExpressionSyntax expression && lowerReturns
                ? rewriter.TranslateExpressionBody(expression)
                : rewriter.Visit(body);
        translated = rewritten is null
            ? string.Empty
            : normalizeWhitespace
                ? rewritten.NormalizeWhitespace(indentation: "    ", eol: "\n").ToFullString().Trim()
                : rewritten.ToFullString().Trim();
        if (rewriter.OutDeclarations.Count != 0)
        {
            translated = string.Join("\n", rewriter.OutDeclarations) + "\n" + translated;
        }

        if (applyGraphicsPostProcessing)
        {
            if (model is null || context is null || parameterMap is null || pushFieldMap is null || storageBufferTargets is null)
            {
                translated = string.Empty;
                usesBuiltin = false;
                reason = "Shader body post-processing requires the active compilation context.";
                return false;
            }

            foreach (var field in pushFieldMap)
            {
                foreach (var parameter in parameterMap.Keys.Where(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, field.Key.ContainingType)))
                {
                    translated = translated.Replace(parameter.Name + "." + field.Key.Name, field.Value);
                }
            }
            foreach (var parameter in parameterMap)
            {
                translated = Regex.Replace(translated, $"\\b{Regex.Escape(parameter.Key.Name)}\\b", parameter.Value, RegexOptions.None);
            }
            foreach (var bufferName in storageBufferTargets)
            {
                translated = Regex.Replace(translated, $"\\b{Regex.Escape(bufferName)}\\s*\\[", bufferName + ".data[", RegexOptions.None);
            }
            foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (_TryBinding(model, context, invocation, stage, out var glslName) && glslName is not null)
                {
                    translated = translated.Replace(invocation.Expression.ToString(), glslName);
                }
            }
            foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var type = model.GetTypeInfo(creation).Type;
                if (type is not null && context.Intrinsics.TryMapType(type, out var glslType))
                {
                    translated = translated.Replace("new " + creation.Type.ToString(), glslType);
                }
            }
            foreach (var declaration in body.DescendantNodes().OfType<VariableDeclarationSyntax>().Where(declaration => declaration.Type.IsVar && declaration.Variables.Count == 1))
            {
                var type = declaration.Variables[0].Initializer is { } initializer
                    ? model.GetTypeInfo(initializer.Value).Type
                    : null;
                if (type is not null && context.Intrinsics.TryMapType(type, out var glslType))
                {
                    translated = Regex.Replace(translated, $"\\b{Regex.Escape(glslType)}(?=[A-Za-z_]\\w*\\s*=)", glslType + " ", RegexOptions.None);
                }
            }
        }

        translated = translated.Replace("\r\n", "\n").Replace("\r", "\n");
        if (normalizeComputeText)
        {
            translated = NormalizeComputeText(translated);
        }

        usesBuiltin = rewriter.UsesBuiltin;
        reason = rewriter.Reason;
        return reason is null;
    }

    public static bool ValidateOutParameters(
        MethodDeclarationSyntax methodSyntax,
        SemanticModel model,
        IMethodSymbol method,
        out string? reason)
    {
        reason = null;
        var outParameters = method.Parameters.Where(parameter => parameter.RefKind == RefKind.Out).ToArray();
        if (outParameters.Length == 0)
        {
            return true;
        }

        DataFlowAnalysis? flow = null;
        if (methodSyntax.Body is { } body)
        {
            if (body.Statements.Count != 0)
            {
                flow = model.AnalyzeDataFlow(body.Statements.First(), body.Statements.Last());
            }
        }
        else if (methodSyntax.ExpressionBody?.Expression is { } expression)
        {
            flow = model.AnalyzeDataFlow(expression);
        }

        if (flow is null || !flow.Succeeded)
        {
            reason = $"Shader helper '{method.Name}' out parameters could not be analyzed for definite assignment.";
            return false;
        }

        foreach (var parameter in outParameters)
        {
            if (!flow.AlwaysAssigned.Contains(parameter) &&
                !flow.AlwaysAssigned.Contains(parameter.OriginalDefinition))
            {
                reason = $"Shader helper '{method.Name}' out parameter '{parameter.Name}' is not definitely assigned on all paths.";
                return false;
            }
        }

        return true;
    }

    private static bool _TryBinding(SemanticModel model, ModuleCompilationContext context, InvocationExpressionSyntax invocation, ShaderStage stage, out string? glslName)
    {
        glslName = null;
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !context.Intrinsics.TryGetIntrinsic(method, out var binding))
        {
            return false;
        }
        if (!binding.SupportsStage(stage))
        {
            return false;
        }
        if (binding.GlslName is "*" or "/" or "+" or "-" or "%")
        {
            return false;
        }
        glslName = binding.GlslName;
        return true;
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_'));

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly ModuleCompilationContext _context;
        private readonly ShaderStage _stage;
        private readonly IReadOnlyDictionary<IParameterSymbol, string> _parameters;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _pushFields;
        private readonly IReadOnlyDictionary<INamedTypeSymbol, string> _structNames;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _structFields;
        private readonly IReadOnlyDictionary<IPropertySymbol, string> _structProperties;
        private readonly IReadOnlyDictionary<IMethodSymbol, string> _helperNames;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _directFields;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _outputFields;
        private readonly Dictionary<ILocalSymbol, string> _locals;
        private readonly bool _computeMode;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _storageFields;
        private readonly HashSet<IParameterSymbol> _storageParameters;
        private readonly HashSet<IParameterSymbol> _outputParameters;
        private readonly INamedTypeSymbol? _returnType;
        private readonly bool _lowerReturns;
        private readonly bool _allowValueReturn;
        private readonly string? _instanceReceiver;
        private readonly HashSet<IPropertySymbol> _staticProperties;
        private readonly IReadOnlyDictionary<IMethodSymbol, bool> _helperReceivers;
        private readonly HashSet<ILocalSymbol> _outLocals;
        private readonly Dictionary<ILocalSymbol, string> _patternVariables;
        private readonly Dictionary<IParameterSymbol, string> _valueSubstitutions;
        private readonly HashSet<ILocalSymbol> _hoistedLocals;
        private readonly List<string> _gotoDeclarations;
        private readonly List<string> _outDeclarations;
        private int _foreachIndex;
        public string? Reason { get; private set; }
        public bool UsesBuiltin { get; private set; }
        public IReadOnlyList<string> OutDeclarations => _outDeclarations;
        public IReadOnlyList<string> GotoDeclarations => _gotoDeclarations;

        public void SetReason(string reason) => Reason ??= reason;

        public Rewriter(
            SemanticModel model,
            ModuleCompilationContext context,
            ShaderStage stage,
            IReadOnlyDictionary<IParameterSymbol, string> parameters,
            IReadOnlyDictionary<IFieldSymbol, string> pushFields,
            IReadOnlyDictionary<INamedTypeSymbol, string> structNames,
            IReadOnlyDictionary<IFieldSymbol, string> structFields,
            IReadOnlyDictionary<IMethodSymbol, string> helperNames,
            IReadOnlyDictionary<IFieldSymbol, string> directFields,
            IReadOnlyDictionary<IFieldSymbol, string> outputFields,
            INamedTypeSymbol? returnType,
            bool lowerReturns,
            IReadOnlyDictionary<ILocalSymbol, string>? locals = null,
            bool computeMode = false,
            IReadOnlyDictionary<IFieldSymbol, string>? storageFields = null,
            IReadOnlyCollection<IParameterSymbol>? storageParameters = null,
            IReadOnlyDictionary<IPropertySymbol, string>? structProperties = null,
            IReadOnlyCollection<IParameterSymbol>? outputParameters = null,
            bool allowValueReturn = false,
            string? instanceReceiver = null,
            IReadOnlyDictionary<IMethodSymbol, bool>? helperReceivers = null)
        {
            _model = model;
            _context = context;
            _stage = stage;
            _parameters = parameters;
            _pushFields = pushFields;
            _structNames = structNames;
            _structFields = structFields;
            _structProperties = structProperties ?? new Dictionary<IPropertySymbol, string>(SymbolEqualityComparer.Default);
            _helperNames = helperNames;
            _directFields = directFields;
            _outputFields = outputFields;
            _locals = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
            if (locals is not null)
            {
                foreach (var local in locals)
                {
                    _locals.Add(local.Key, local.Value);
                }
            }
            _computeMode = computeMode;
            _storageFields = storageFields ?? new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
            _storageParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            if (storageParameters is not null)
            {
                foreach (var parameter in storageParameters)
                {
                    _storageParameters.Add(parameter);
                }
            }
            _outputParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            if (outputParameters is not null)
            {
                foreach (var parameter in outputParameters)
                {
                    _outputParameters.Add(parameter);
                }
            }
            _returnType = returnType;
            _lowerReturns = lowerReturns;
            _allowValueReturn = allowValueReturn;
            _instanceReceiver = instanceReceiver;
            _staticProperties = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
            _helperReceivers = helperReceivers ?? new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
            _outLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            _patternVariables = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
            _valueSubstitutions = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            _hoistedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            _gotoDeclarations = new List<string>();
            _outDeclarations = new List<string>();
        }

        public bool ContainsOutLocalDeclaration(SyntaxNode root)
            => root.DescendantNodesAndSelf()
                .OfType<ArgumentSyntax>()
                .Any(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) &&
                    argument.Expression is DeclarationExpressionSyntax);

        public void PredeclareOutLocals(SyntaxNode root)
        {
            foreach (var argument in root.DescendantNodesAndSelf()
                         .OfType<ArgumentSyntax>()
                         .Where(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                         .OrderBy(argument => argument.SpanStart))
            {
                var symbol = argument.Expression switch
                {
                    DeclarationExpressionSyntax declaration when declaration.Designation is SingleVariableDesignationSyntax designation
                        => GetDeclaredSymbol(designation),
                    IdentifierNameSyntax identifier => GetSymbolInfo(identifier).Symbol,
                    _ => null
                };
                if (symbol is not ILocalSymbol local)
                {
                    continue;
                }

                if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarationWithInitializer &&
                    declarationWithInitializer.Initializer is not null)
                {
                    continue;
                }

                if (!_outLocals.Add(local))
                {
                    continue;
                }

                if (!TryMap(local.Type, out var glslType))
                {
                    Reason ??= $"Shader out variable '{local.Name}' has an unsupported type.";
                    continue;
                }

                var localName = CreateLocalName(local.Name);
                _locals[local] = localName;
                _outDeclarations.Add($"{glslType} {localName};");
            }
        }

        public bool PrepareGotoLocals(BlockSyntax root)
        {
            foreach (var declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
            {
                if (declaration.Parent is not LocalDeclarationStatementSyntax &&
                    declaration.Parent is not ForStatementSyntax)
                {
                    continue;
                }

                if (declaration.Variables.Count != 1 ||
                    GetDeclaredSymbol(declaration.Variables[0]) is not ILocalSymbol local)
                {
                    Reason ??= "Goto lowering requires one shader-value local per declaration.";
                    return false;
                }

                if (_outLocals.Contains(local) || _hoistedLocals.Contains(local))
                {
                    continue;
                }

                var variable = declaration.Variables[0];
                var type = declaration.Type.IsVar
                    ? variable.Initializer is { } initializer
                        ? GetTypeInfo(initializer.Value).ConvertedType ?? GetTypeInfo(initializer.Value).Type
                        : null
                    : GetTypeInfo(declaration.Type).Type;
                if (type is null || !TryMap(type, out var glslType) ||
                    !TryCreateZeroValue(type, out var zero))
                {
                    Reason ??= $"Goto local '{local.Name}' has an unsupported shader value type.";
                    return false;
                }

                var localName = CreateLocalName(local.Name);
                _locals[local] = localName;
                _hoistedLocals.Add(local);
                _gotoDeclarations.Add($"{glslType} {localName} = {zero.ToFullString()};");
            }

            return true;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = GetSymbolInfo(node).Symbol;
            if (symbol is ILocalSymbol local && _locals.TryGetValue(local, out var localName))
            {
                return SyntaxFactory.ParseName(localName);
            }

            if (symbol is ILocalSymbol patternLocal && _patternVariables.TryGetValue(patternLocal, out var patternExpression))
            {
                return SyntaxFactory.ParseExpression(patternExpression);
            }

            if (symbol is IParameterSymbol valueParameter &&
                _valueSubstitutions.TryGetValue(valueParameter, out var substitutedValue))
            {
                return SyntaxFactory.ParseExpression(substitutedValue);
            }

            if (symbol is IParameterSymbol parameter && _parameters.TryGetValue(parameter, out var parameterName))
            {
                return SyntaxFactory.ParseName(parameterName);
            }

            if (_instanceReceiver is not null &&
                symbol is IFieldSymbol field &&
                !field.IsStatic &&
                _structFields.TryGetValue(field, out var fieldName))
            {
                return SyntaxFactory.ParseExpression(_instanceReceiver + "." + fieldName);
            }

            if (_instanceReceiver is not null &&
                symbol is IPropertySymbol property &&
                !property.IsStatic &&
                _structProperties.TryGetValue(property, out var propertyName))
            {
                return SyntaxFactory.ParseExpression(_instanceReceiver + "." + propertyName);
            }

            if (symbol is IPropertySymbol instanceProperty &&
                !instanceProperty.IsStatic &&
                TryTranslateInstanceProperty(instanceProperty, _instanceReceiver, out var instancePropertyExpression))
            {
                return instancePropertyExpression;
            }

            if (symbol is IPropertySymbol intrinsicProperty &&
                intrinsicProperty.IsStatic &&
                TryTranslateIntrinsicProperty(intrinsicProperty, out var intrinsicPropertyExpression))
            {
                return intrinsicPropertyExpression;
            }

            if (symbol is IPropertySymbol staticProperty &&
                staticProperty.IsStatic &&
                TryTranslateStaticProperty(staticProperty, out var propertyExpression))
            {
                return propertyExpression;
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            if (_instanceReceiver is not null)
            {
                return SyntaxFactory.ParseName(_instanceReceiver);
            }

            Reason ??= "Static shader helpers cannot use an instance receiver.";
            return base.VisitThisExpression(node);
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            if (!_computeMode)
            {
                return base.VisitBlock(node);
            }

            var statements = new List<StatementSyntax>(node.Statements.Count);
            foreach (var statement in node.Statements)
            {
                if (statement is not BlockSyntax and not LocalDeclarationStatementSyntax and
                    not IfStatementSyntax and not ForStatementSyntax and not WhileStatementSyntax and
                    not DoStatementSyntax and not ForEachStatementSyntax and not SwitchStatementSyntax and not ExpressionStatementSyntax and
                    not ReturnStatementSyntax and not BreakStatementSyntax and not ContinueStatementSyntax)
                {
                    Reason ??= "Only declarations, conditionals, switch, loops, and assignments are supported in compute shader bodies.";
                    continue;
                }

                if (Visit(statement) is StatementSyntax rewritten)
                {
                    statements.Add(rewritten);
                }
            }

            return SyntaxFactory.Block(statements);
        }

        public override SyntaxNode? VisitBreakStatement(BreakStatementSyntax node)
            => _computeMode ? node : base.VisitBreakStatement(node);

        public override SyntaxNode? VisitContinueStatement(ContinueStatementSyntax node)
            => _computeMode ? node : base.VisitContinueStatement(node);

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            if (Visit(node.Condition) is not ExpressionSyntax condition)
            {
                Reason ??= "While-loop condition could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            if (Visit(node.Statement) is not StatementSyntax body)
            {
                Reason ??= "While-loop body could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            return SyntaxFactory.ParseStatement(
                $"while ({condition.ToFullString().Trim()}) {body.ToFullString().Trim()}");
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            if (Visit(node.Condition) is not ExpressionSyntax condition)
            {
                Reason ??= "Do-while condition could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            if (Visit(node.Statement) is not StatementSyntax body)
            {
                Reason ??= "Do-while body could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            return SyntaxFactory.ParseStatement(
                $"do {body.ToFullString().Trim()} while ({condition.ToFullString().Trim()});");
        }

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
        {
            if (Visit(node.Expression) is not ExpressionSyntax expression)
            {
                Reason ??= "Switch expression could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            var branches = new List<string>();
            string? defaultBody = null;
            var pendingLabels = new List<SwitchLabelSyntax>();
            foreach (var section in node.Sections)
            {
                var labels = new List<SwitchLabelSyntax>(pendingLabels.Count + section.Labels.Count);
                labels.AddRange(pendingLabels);
                labels.AddRange(section.Labels);
                pendingLabels.Clear();

                var lastStatement = section.Statements.Count == 0
                    ? null
                    : section.Statements[section.Statements.Count - 1];
                if (lastStatement is not null && !IsSwitchTerminator(lastStatement))
                {
                    Reason ??= "Switch sections must terminate with break, return, throw, or goto; fall-through is not supported.";
                    return SyntaxFactory.EmptyStatement();
                }

                if (section.Statements.Count == 0 &&
                    labels.OfType<DefaultSwitchLabelSyntax>().Count() == 0)
                {
                    pendingLabels.AddRange(labels);
                    continue;
                }

                if (labels.Count == 0 || labels.OfType<DefaultSwitchLabelSyntax>().Count() > 1)
                {
                    Reason ??= "Switch sections must contain at least one label and only one default label.";
                    return SyntaxFactory.EmptyStatement();
                }

                var bodyStatements = new List<string>();
                foreach (var statement in section.Statements)
                {
                    if (statement.IsKind(SyntaxKind.BreakStatement))
                    {
                        continue;
                    }

                    if (Visit(statement) is not StatementSyntax rewritten)
                    {
                        Reason ??= "Switch section contains an unsupported statement.";
                        return SyntaxFactory.EmptyStatement();
                    }

                    bodyStatements.Add(rewritten.ToFullString().Trim());
                }

                var body = string.Join(" ", bodyStatements);
                var defaultLabel = labels.OfType<DefaultSwitchLabelSyntax>().SingleOrDefault();
                var caseConditions = new List<string>();
                foreach (var label in labels)
                {
                    if (label is CaseSwitchLabelSyntax caseLabel)
                    {
                        if (Visit(caseLabel.Value) is not ExpressionSyntax caseValue)
                        {
                            Reason ??= "Switch case label could not be translated.";
                            return SyntaxFactory.EmptyStatement();
                        }

                        caseConditions.Add($"({expression.ToFullString().Trim()} == {caseValue.ToFullString().Trim()})");
                        continue;
                    }

                    if (label is CasePatternSwitchLabelSyntax patternLabel &&
                        TryTranslatePattern(patternLabel.Pattern, expression.ToFullString().Trim(), out var patternCondition))
                    {
                        if (patternLabel.WhenClause is { Condition: { } guard } &&
                            Visit(guard) is ExpressionSyntax translatedGuard)
                        {
                            patternCondition = $"({patternCondition}) && ({translatedGuard.ToFullString().Trim()})";
                        }

                        caseConditions.Add(patternCondition);
                        continue;
                    }

                    if (label is not DefaultSwitchLabelSyntax)
                    {
                        Reason ??= "Switch supports constant or translatable pattern case labels.";
                        return SyntaxFactory.EmptyStatement();
                    }
                }

                if (defaultLabel is not null)
                {
                    if (defaultBody is not null)
                    {
                        Reason ??= "Switch supports only one default section.";
                        return SyntaxFactory.EmptyStatement();
                    }

                    if (caseConditions.Count != 0)
                    {
                        branches.Add($"if ({string.Join(" || ", caseConditions)}) {{ {body} }}");
                    }

                    defaultBody = body;
                    continue;
                }

                if (caseConditions.Count == 0)
                {
                    Reason ??= "Switch supports constant or translatable pattern case labels, or one default label per section.";
                    return SyntaxFactory.EmptyStatement();
                }

                var condition = string.Join(" || ", caseConditions);
                branches.Add($"if ({condition}) {{ {body} }}");
            }

            if (pendingLabels.Count != 0)
            {
                Reason ??= "Switch cannot end with an empty fall-through section.";
                return SyntaxFactory.EmptyStatement();
            }

            if (defaultBody is not null)
            {
                branches.Add($"{{ {defaultBody} }}");
            }

            return SyntaxFactory.ParseStatement(string.Join(" else ", branches));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            if (GetDeclaredSymbol(node) is not ILocalSymbol local ||
                GetTypeInfo(node.Expression).Type is not INamedTypeSymbol bufferType ||
                !IsStorageBufferType(bufferType, _context) ||
                bufferType.TypeArguments.Length != 1 ||
                !TryGetShaderType(bufferType.TypeArguments[0], out var elementGlslType))
            {
                Reason ??= "Foreach is supported only for storage buffers with one shader-visible element type.";
                return SyntaxFactory.EmptyStatement();
            }

            var source = Visit(node.Expression) as ExpressionSyntax;
            if (source is null)
            {
                Reason ??= "Foreach storage-buffer source could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            var localName = CreateLocalName(local.Name);
            var indexName = "__delta_foreach_" + _foreachIndex++;
            _locals[local] = localName;
            var body = Visit(node.Statement) as StatementSyntax;
            _locals.Remove(local);
            if (body is null)
            {
                Reason ??= "Foreach storage-buffer body could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            return SyntaxFactory.ParseStatement(
                $"for (int {indexName} = 0; {indexName} < {source.ToFullString().Trim()}.data.length(); ++{indexName}) {{ {elementGlslType} {localName} = {source.ToFullString().Trim()}.data[{indexName}]; {body.ToFullString().Trim()} }}");
        }

        private bool TryGetShaderType(ITypeSymbol type, out string glslType)
            => TryMap(type, out glslType) ||
               type is INamedTypeSymbol namedType && _structNames.TryGetValue(namedType, out glslType);

        private static bool IsSwitchTerminator(StatementSyntax statement)
            => statement is BreakStatementSyntax or ContinueStatementSyntax or ReturnStatementSyntax;

        public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (Visit(node.Expression) is not ExpressionSyntax expression ||
                !TryTranslatePattern(node.Pattern, expression.ToFullString().Trim(), out var translatedPattern))
            {
                Reason ??= "Only constant, relational, and/or patterns are supported in shader code.";
                return SyntaxFactory.ParseExpression("false");
            }

            return SyntaxFactory.ParseExpression(translatedPattern);
        }

        public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            if (Visit(node.GoverningExpression) is not ExpressionSyntax governing)
            {
                Reason ??= "Switch-expression governing value could not be translated.";
                return SyntaxFactory.ParseExpression("0");
            }

            var governingText = governing.ToFullString().Trim();
            string? result = null;
            for (var index = node.Arms.Count - 1; index >= 0; index--)
            {
                var arm = node.Arms[index];
                if (Visit(arm.Expression) is not ExpressionSyntax armExpression)
                {
                    Reason ??= "Switch-expression arm could not be translated.";
                    return SyntaxFactory.ParseExpression("0");
                }

                var value = armExpression.ToFullString().Trim();
                if (arm.Pattern is DiscardPatternSyntax)
                {
                    result = value;
                    continue;
                }

                if (!TryTranslatePattern(arm.Pattern, governingText, out var pattern))
                {
                    Reason ??= "Switch-expression contains an unsupported pattern.";
                    return SyntaxFactory.ParseExpression("0");
                }

                if (arm.WhenClause is { } whenClause)
                {
                    if (Visit(whenClause.Condition) is not ExpressionSyntax whenCondition)
                    {
                        Reason ??= "Switch-expression guard could not be translated.";
                        return SyntaxFactory.ParseExpression("0");
                    }

                    pattern = $"({pattern} && {whenCondition.ToFullString().Trim()})";
                }

                if (result is null)
                {
                    Reason ??= "Switch-expression requires a final discard arm.";
                    return SyntaxFactory.ParseExpression("0");
                }

                result = $"({pattern} ? {value} : {result})";
            }

            return result is null
                ? SyntaxFactory.ParseExpression("0")
                : SyntaxFactory.ParseExpression(result);
        }

        private bool TryTranslatePattern(PatternSyntax pattern, string expression, out string translated)
        {
            switch (pattern)
            {
                case DiscardPatternSyntax:
                    translated = "true";
                    return true;
                case TypePatternSyntax typePattern:
                    if (GetTypeInfo(typePattern.Type).Type is ITypeSymbol type && TryMap(type, out _))
                    {
                        translated = "true";
                        return true;
                    }

                    break;
                case DeclarationPatternSyntax declaration:
                    if (GetTypeInfo(declaration.Type).Type is ITypeSymbol declarationType &&
                        TryMap(declarationType, out _) &&
                        declaration.Designation is SingleVariableDesignationSyntax designation &&
                        GetDeclaredSymbol(designation) is ILocalSymbol declarationLocal)
                    {
                        _patternVariables[declarationLocal] = expression;
                        translated = "true";
                        return true;
                    }

                    break;
                case VarPatternSyntax variablePattern when
                    variablePattern.Designation is SingleVariableDesignationSyntax variableDesignation &&
                    GetDeclaredSymbol(variableDesignation) is ILocalSymbol variableLocal:
                    _patternVariables[variableLocal] = expression;
                    translated = "true";
                    return true;
                case ConstantPatternSyntax constant:
                    if (Visit(constant.Expression) is ExpressionSyntax constantExpression)
                    {
                        translated = $"({expression} == {constantExpression.ToFullString().Trim()})";
                        return true;
                    }

                    break;
                case RelationalPatternSyntax relational:
                    if (Visit(relational.Expression) is ExpressionSyntax relationalExpression)
                    {
                        translated = $"({expression} {relational.OperatorToken.Text} {relationalExpression.ToFullString().Trim()})";
                        return true;
                    }

                    break;
                case BinaryPatternSyntax binary when binary.OperatorToken.IsKind(SyntaxKind.AndKeyword) ||
                    binary.OperatorToken.IsKind(SyntaxKind.OrKeyword):
                    if (TryTranslatePattern(binary.Left, expression, out var left) &&
                        TryTranslatePattern(binary.Right, expression, out var right))
                    {
                        var op = binary.OperatorToken.IsKind(SyntaxKind.AndKeyword) ? "&&" : "||";
                        translated = $"({left} {op} {right})";
                        return true;
                    }

                    break;
                case RecursivePatternSyntax recursive when
                    (recursive.Type is null ||
                     GetTypeInfo(recursive.Type).Type is ITypeSymbol recursiveType && TryGetShaderType(recursiveType, out _)) &&
                    recursive.PositionalPatternClause is null && recursive.PropertyPatternClause is { } properties:
                    var propertyConditions = new List<string>(properties.Subpatterns.Count);
                    foreach (var subpattern in properties.Subpatterns)
                    {
                        var member = GetSymbolInfo(subpattern.NameColon!.Name).Symbol;
                        if (member is not IFieldSymbol and not IPropertySymbol)
                        {
                            translated = string.Empty;
                            return false;
                        }

                        var memberName = member switch
                        {
                            IFieldSymbol field when _structFields.TryGetValue(field, out var fieldName) => fieldName,
                            IPropertySymbol property when _structProperties.TryGetValue(property, out var propertyName) => propertyName,
                            _ => member.Name
                        };
                        if (!TryTranslatePattern(subpattern.Pattern, expression + "." + memberName, out var propertyCondition))
                        {
                            translated = string.Empty;
                            return false;
                        }

                        propertyConditions.Add(propertyCondition);
                    }

                    translated = propertyConditions.Count == 0
                        ? "true"
                        : "(" + string.Join(" && ", propertyConditions) + ")";
                    return true;
                case UnaryPatternSyntax negated when negated.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                    TryTranslatePattern(negated.Pattern, expression, out var negatedPattern):
                    translated = "!(" + negatedPattern + ")";
                    return true;
                case ParenthesizedPatternSyntax parenthesized:
                    return TryTranslatePattern(parenthesized.Pattern, expression, out translated);
            }

            translated = string.Empty;
            return false;
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (node.Declaration.Variables.Count == 1 &&
                node.Declaration.Variables[0].Initializer is null &&
                GetDeclaredSymbol(node.Declaration.Variables[0]) is ILocalSymbol local &&
                _outLocals.Contains(local))
            {
                return SyntaxFactory.EmptyStatement();
            }

            if (!_computeMode)
            {
                if (node.Declaration.Variables.Count == 1 &&
                    GetDeclaredSymbol(node.Declaration.Variables[0]) is ILocalSymbol hoistedLocal &&
                    _hoistedLocals.Contains(hoistedLocal))
                {
                    return TranslateLocalDeclaration(node.Declaration);
                }

                if (node.Declaration.Variables.Count == 1 &&
                    node.Declaration.Variables[0].Initializer is { } initializer)
                {
                    var type = node.Declaration.Type.IsVar
                        ? GetTypeInfo(initializer.Value).ConvertedType ?? GetTypeInfo(initializer.Value).Type
                        : GetTypeInfo(node.Declaration.Type).Type;
                    if (_returnType is not null &&
                        type is not null &&
                        SymbolEqualityComparer.Default.Equals(type, _returnType))
                    {
                        return SyntaxFactory.EmptyStatement();
                    }

                    if (type is INamedTypeSymbol statelessType &&
                        ShaderStructSupport.IsStateless(statelessType) &&
                        !TryMap(statelessType, out _))
                    {
                        return SyntaxFactory.EmptyStatement();
                    }
                }

                return base.VisitLocalDeclarationStatement(node);
            }

            return TranslateLocalDeclaration(node.Declaration);
        }

        private StatementSyntax? TranslateLocalDeclaration(VariableDeclarationSyntax declaration)
        {
            if (declaration.Variables.Count != 1)
            {
                Reason ??= "Local declarations require exactly one shader value per declaration.";
                return null;
            }

            var variable = declaration.Variables[0];
            if (GetDeclaredSymbol(variable) is not ILocalSymbol local)
            {
                Reason ??= "Compute shader local declaration has no Roslyn symbol.";
                return null;
            }

            if (_hoistedLocals.Contains(local))
            {
                if (variable.Initializer is not { } hoistedInitializer ||
                    Visit(hoistedInitializer.Value) is not ExpressionSyntax rewrittenHoistedInitializer)
                {
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.ParseStatement(
                    $"{_locals[local]} = {rewrittenHoistedInitializer.ToFullString().Trim()};");
            }

            var type = declaration.Type.IsVar
                ? variable.Initializer is { } inferredInitializer
                    ? GetTypeInfo(inferredInitializer.Value).ConvertedType ?? GetTypeInfo(inferredInitializer.Value).Type
                    : null
                : GetTypeInfo(declaration.Type).Type;
            if (_computeMode && type is INamedTypeSymbol statelessType &&
                ShaderStructSupport.IsStateless(statelessType) &&
                !TryMap(statelessType, out _))
            {
                return SyntaxFactory.EmptyStatement();
            }

            if (type is null || !TryMap(type, out var glslType))
            {
                Reason ??= "Local declaration has an unsupported shader type.";
                return null;
            }

            var localName = CreateLocalName(local.Name);
            _locals[local] = localName;
            if (variable.Initializer is null)
            {
                if (!TryCreateZeroValue(type, out var zero))
                {
                    return null;
                }

                return SyntaxFactory.ParseStatement($"{glslType} {localName} = {zero.ToFullString()};");
            }

            if (Visit(variable.Initializer.Value) is not ExpressionSyntax rewrittenInitializer)
            {
                Reason ??= "Compute shader local initializer could not be translated.";
                return null;
            }

            return SyntaxFactory.ParseStatement($"{glslType} {localName} = {rewrittenInitializer.ToFullString().Trim()};");
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            if (!_computeMode)
            {
                return base.VisitForStatement(node);
            }

            if (node.Declaration is null || node.Declaration.Variables.Count != 1)
            {
                Reason ??= "Compute shader for loops require exactly one initialized local declaration.";
                return SyntaxFactory.EmptyStatement();
            }

            var initializer = TranslateLocalDeclaration(node.Declaration);
            if (initializer is not StatementSyntax initializerStatement)
            {
                Reason ??= "Compute shader for-loop initializer could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            var initializerText = initializerStatement.ToFullString().Trim();
            if (initializerText.EndsWith(";", StringComparison.Ordinal))
            {
                initializerText = initializerText.Substring(0, initializerText.Length - 1).TrimEnd();
            }

            if (node.Condition is null || Visit(node.Condition) is not ExpressionSyntax condition)
            {
                Reason ??= "Compute shader for loops require a translatable condition.";
                return SyntaxFactory.EmptyStatement();
            }

            if (node.Incrementors.Count != 1 || !IsSupportedForIncrement(node.Incrementors[0]) ||
                Visit(node.Incrementors[0]) is not ExpressionSyntax increment)
            {
                Reason ??= "Compute shader for loops support one local ++, --, +=, or -= increment.";
                return SyntaxFactory.EmptyStatement();
            }

            if (Visit(node.Statement) is not StatementSyntax body)
            {
                Reason ??= "Compute shader for-loop body could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            return SyntaxFactory.ParseStatement(
                $"for ({initializerText}; {condition.ToFullString().Trim()}; {increment.ToFullString().Trim()}) {body.ToFullString().Trim()}");
        }

        private bool IsSupportedForIncrement(ExpressionSyntax expression)
        {
            if (expression is PrefixUnaryExpressionSyntax prefix &&
                prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                expression is PrefixUnaryExpressionSyntax prefixDecrement &&
                prefixDecrement.IsKind(SyntaxKind.PreDecrementExpression) ||
                expression is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                expression is PostfixUnaryExpressionSyntax postfixDecrement &&
                postfixDecrement.IsKind(SyntaxKind.PostDecrementExpression))
            {
                var operand = expression switch
                {
                    PrefixUnaryExpressionSyntax prefixExpression => prefixExpression.Operand,
                    PostfixUnaryExpressionSyntax postfixExpression => postfixExpression.Operand,
                    _ => null
                };
                return IsLocalIdentifier(operand);
            }

            if (expression is AssignmentExpressionSyntax assignment &&
                (assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)))
            {
                return IsLocalIdentifier(assignment.Left);
            }

            return false;
        }

        private bool IsLocalIdentifier(ExpressionSyntax? expression)
            => expression is IdentifierNameSyntax identifier &&
                GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
                _locals.ContainsKey(local);

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is InvocationExpressionSyntax invocation &&
                Visit(invocation) is ExpressionSyntax translatedInvocation)
            {
                return SyntaxFactory.ParseStatement(translatedInvocation.ToFullString().Trim() + ";");
            }

            if (!_computeMode)
            {
                if (node.Expression is AssignmentExpressionSyntax graphicsAssignment &&
                    (graphicsAssignment.Left is MemberAccessExpressionSyntax or IdentifierNameSyntax) &&
                    GetSymbolInfo(graphicsAssignment.Left).Symbol is IFieldSymbol or IPropertySymbol &&
                    IsSupportedAssignmentOperator(graphicsAssignment.Kind()) &&
                    Visit(graphicsAssignment.Left) is ExpressionSyntax rewrittenGraphicsMember &&
                    Visit(graphicsAssignment.Right) is ExpressionSyntax rewrittenValue)
                {
                    return SyntaxFactory.ParseStatement(
                        $"{rewrittenGraphicsMember.ToFullString().Trim()} {graphicsAssignment.OperatorToken.Text} {rewrittenValue.ToFullString().Trim()};");
                }

                return base.VisitExpressionStatement(node);
            }

            if (node.Expression is not AssignmentExpressionSyntax assignment)
            {
                Reason ??= "Compute shader executable expressions must be assignments or helper calls with a discarded result.";
                return SyntaxFactory.EmptyStatement();
            }

            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax discard &&
                discard.Identifier.ValueText == "_" &&
                Visit(assignment.Right) is ExpressionSyntax discardedExpression)
            {
                return SyntaxFactory.ParseStatement(discardedExpression.ToFullString().Trim() + ";");
            }

            if (assignment.Left is ElementAccessExpressionSyntax elementAccess)
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || !IsComputeStorageTarget(elementAccess.Expression))
                {
                    Reason ??= "Only simple indexed storage-buffer assignments are supported in compute shader bodies.";
                    return SyntaxFactory.EmptyStatement();
                }

                if (Visit(elementAccess) is not ExpressionSyntax rewrittenTarget ||
                    Visit(assignment.Right) is not ExpressionSyntax rewrittenValue)
                {
                    Reason ??= "Indexed storage-buffer assignment could not be translated.";
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.ParseStatement($"{rewrittenTarget.ToFullString().Trim()} = {rewrittenValue.ToFullString().Trim()};");
            }

            if (assignment.Left is IdentifierNameSyntax identifier &&
                GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
                _locals.ContainsKey(local))
            {
                if (!IsComputeLocalAssignment(assignment))
                {
                    Reason ??= "Only simple and arithmetic local assignments are supported in compute shader bodies.";
                    return SyntaxFactory.EmptyStatement();
                }

                if (Visit(assignment.Right) is not ExpressionSyntax rewrittenValue)
                {
                    Reason ??= "Local assignment value could not be translated.";
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.ParseStatement($"{_locals[local]} {assignment.OperatorToken.Text} {rewrittenValue.ToFullString().Trim()};");
            }

            if (assignment.Left is IdentifierNameSyntax outputIdentifier &&
                GetSymbolInfo(outputIdentifier).Symbol is IParameterSymbol outputParameter &&
                _outputParameters.Contains(outputParameter) &&
                _parameters.TryGetValue(outputParameter, out var outputName))
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    Reason ??= "Compute helper out parameters support only simple assignments.";
                    return SyntaxFactory.EmptyStatement();
                }

                if (Visit(assignment.Right) is not ExpressionSyntax rewrittenValue)
                {
                    Reason ??= "Compute helper out-parameter assignment could not be translated.";
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.ParseStatement($"{outputName} = {rewrittenValue.ToFullString().Trim()};");
            }

            if ((assignment.Left is MemberAccessExpressionSyntax or IdentifierNameSyntax) &&
                GetSymbolInfo(assignment.Left).Symbol is IFieldSymbol or IPropertySymbol &&
                Visit(assignment.Left) is ExpressionSyntax rewrittenMember &&
                Visit(assignment.Right) is ExpressionSyntax rewrittenMemberValue &&
                IsSupportedAssignmentOperator(assignment.Kind()))
            {
                return SyntaxFactory.ParseStatement(
                    $"{rewrittenMember.ToFullString().Trim()} {assignment.OperatorToken.Text} {rewrittenMemberValue.ToFullString().Trim()};");
            }

            Reason ??= "Compute shader assignments must target a local or indexed storage buffer.";
            return SyntaxFactory.EmptyStatement();
        }

        public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            if (!_computeMode)
            {
                return base.VisitElementAccessExpression(node);
            }

            if (node.ArgumentList.Arguments.Count != 1 || !IsComputeStorageTarget(node.Expression))
            {
                Reason ??= "Storage-buffer access requires exactly one index.";
                return SyntaxFactory.EmptyStatement();
            }

            if (Visit(node.Expression) is not ExpressionSyntax rewrittenTarget ||
                Visit(node.ArgumentList.Arguments[0].Expression) is not ExpressionSyntax rewrittenIndex)
            {
                Reason ??= "Indexed storage-buffer access could not be translated.";
                return SyntaxFactory.EmptyStatement();
            }

            return SyntaxFactory.ParseExpression($"{rewrittenTarget.ToFullString().Trim()}.data[{rewrittenIndex.ToFullString().Trim()}]");
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                var typeInfo = GetTypeInfo(node);
                var type = typeInfo.ConvertedType ?? typeInfo.Type;
                if (type is not null && TryCreateZeroValue(type, out var zero))
                {
                    return zero;
                }
            }

            if (node.IsKind(SyntaxKind.NumericLiteralExpression) &&
                node.Token.Value is float or double or decimal)
            {
                var literal = node.Token.Value switch
                {
                    float value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    double value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    decimal value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => node.Token.Text
                };

                if (!literal.Contains('.') && !literal.Contains('e') && !literal.Contains('E'))
                {
                    literal += ".0";
                }

                return SyntaxFactory.ParseExpression(literal).WithTriviaFrom(node);
            }

            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            var type = GetTypeInfo(node).Type;
            if (type is not null && TryCreateZeroValue(type, out var zero))
            {
                return zero;
            }

            Reason ??= "Default shader value has an unsupported type.";
            return base.VisitDefaultExpression(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbol = GetSymbolInfo(node).Symbol;
            if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum, HasConstantValue: true } enumField &&
                ShaderEnumSupport.TryMap(enumField.ContainingType, out var enumType))
            {
                return SyntaxFactory.ParseExpression(
                    ShaderEnumSupport.FormatConstant(enumField.ConstantValue, enumType));
            }

            if (TryTranslateShaderBuiltinMember(node, out var builtinExpression))
            {
                return SyntaxFactory.ParseExpression(builtinExpression);
            }

            if (symbol is IPropertySymbol intrinsicProperty &&
                intrinsicProperty.IsStatic &&
                TryTranslateIntrinsicProperty(intrinsicProperty, out var intrinsicPropertyExpression))
            {
                return intrinsicPropertyExpression;
            }

            if (symbol is IPropertySymbol staticProperty &&
                staticProperty.IsStatic &&
                TryTranslateStaticProperty(staticProperty, out var propertyExpression))
            {
                return propertyExpression;
            }

            if (symbol is IPropertySymbol or IFieldSymbol &&
                _context.Intrinsics.TryGetIntrinsic(symbol, out var swizzleBinding) &&
                swizzleBinding.Category == IntrinsicCategory.Swizzle)
            {
                if (!swizzleBinding.SupportsStage(_stage))
                {
                    Reason ??= $"Shader swizzle '{node.Name.Identifier.ValueText}' is not valid in {_stage} stage.";
                    return SyntaxFactory.EmptyStatement();
                }

                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                return SyntaxFactory.ParseExpression(receiver + "." + swizzleBinding.GlslName);
            }

            if (symbol is IFieldSymbol field && _pushFields.TryGetValue(field, out var fieldName))
            {
                return SyntaxFactory.ParseExpression(fieldName);
            }
            if (symbol is IFieldSymbol directField && _directFields.TryGetValue(directField, out var directFieldName))
            {
                return SyntaxFactory.ParseExpression(directFieldName);
            }
            if (symbol is IFieldSymbol structField && _structFields.TryGetValue(structField, out var structFieldName))
            {
                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                if (structFieldName.Length == 0)
                {
                    return SyntaxFactory.ParseExpression(receiver);
                }

                return SyntaxFactory.ParseExpression(receiver + "." + structFieldName);
            }
            if (symbol is IPropertySymbol structProperty && _structProperties.TryGetValue(structProperty, out var structPropertyName))
            {
                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                return SyntaxFactory.ParseExpression(receiver + "." + structPropertyName);
            }

            if (node.Name.Identifier.ValueText == "Length" &&
                GetSymbolInfo(node.Expression).Symbol is IFieldSymbol resourceField &&
                _storageFields.TryGetValue(resourceField, out var resourceName))
            {
                return SyntaxFactory.ParseExpression(resourceName + ".data.length()");
            }

            if (symbol is IPropertySymbol instanceProperty &&
                !instanceProperty.IsStatic &&
                instanceProperty.ContainingType is INamedTypeSymbol containingType &&
                _structNames.ContainsKey(containingType) &&
                IsPropertyDeclaredInActiveCompilation(instanceProperty))
            {
                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                if (TryTranslateInstanceProperty(instanceProperty, receiver, out var instancePropertyExpression))
                {
                    return instancePropertyExpression;
                }
            }

            return base.VisitMemberAccessExpression(node);
        }

        private bool TryTranslateIntrinsicProperty(
            IPropertySymbol property,
            [NotNullWhen(true)] out ExpressionSyntax? expression)
        {
            expression = null;
            if (!_context.Intrinsics.TryGetIntrinsic(property, out var binding) ||
                binding.Category != IntrinsicCategory.Function)
            {
                return false;
            }

            if (!binding.SupportsStage(_stage))
            {
                Reason ??= $"Intrinsic '{property.Name}' is not valid in {_stage} stage.";
                return false;
            }

            expression = SyntaxFactory.ParseExpression(binding.GlslName);
            return true;
        }

        private bool TryTranslateStaticProperty(
            IPropertySymbol property,
            [NotNullWhen(true)] out ExpressionSyntax? expression)
        {
            expression = null;
            if (!_staticProperties.Add(property))
            {
                Reason ??= $"Static shader property '{property.Name}' is recursive.";
                return false;
            }

            try
            {
                if (property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax syntax)
                {
                    Reason ??= $"Static shader property '{property.Name}' must be declared in the shader source project.";
                    return false;
                }

                var getter = GetSimpleGetterExpression(syntax);
                if (getter is null && syntax.Initializer?.Value is { } initializer &&
                    HasConstantValue(initializer))
                {
                    getter = initializer;
                }
                if (getter is null)
                {
                    Reason ??= $"Static shader property '{property.Name}' must have a compile-time getter expression.";
                    return false;
                }

                getter = RebindExpressionToActiveCompilation(getter);
                if (getter is null)
                {
                    Reason ??= $"Static shader property '{property.Name}' getter is not part of the active Roslyn compilation.";
                    return false;
                }

                expression = Visit(getter) as ExpressionSyntax;
                if (expression is null)
                {
                    Reason ??= $"Static shader property '{property.Name}' has an unsupported getter expression.";
                    return false;
                }

                return true;
            }
            finally
            {
                _staticProperties.Remove(property);
            }
        }

        private bool TryTranslateInstanceProperty(
            IPropertySymbol property,
            string? receiver,
            [NotNullWhen(true)] out ExpressionSyntax? expression)
        {
            expression = null;
            if (property.GetMethod is null || property.IsStatic || property.Parameters.Length != 0 ||
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax syntax)
            {
                return false;
            }

            var getter = GetSimpleGetterExpression(syntax);
            if (getter is null)
            {
                return false;
            }

            getter = RebindExpressionToActiveCompilation(getter);
            if (getter is null)
            {
                Reason ??= $"Instance shader property '{property.Name}' getter is not part of the active Roslyn compilation.";
                return false;
            }

            var propertyRewriter = new Rewriter(
                _model,
                _context,
                _stage,
                _parameters,
                _pushFields,
                _structNames,
                _structFields,
                _helperNames,
                _directFields,
                _outputFields,
                _returnType,
                _lowerReturns,
                _locals,
                _computeMode,
                _storageFields,
                _storageParameters,
                _structProperties,
                _outputParameters,
                _allowValueReturn,
                receiver,
                _helperReceivers);
            expression = propertyRewriter.Visit(getter) as ExpressionSyntax;
            if (propertyRewriter.Reason is not null)
            {
                Reason ??= propertyRewriter.Reason;
                expression = null;
                return false;
            }

            return expression is not null;
        }

        private static ExpressionSyntax? GetSimpleGetterExpression(PropertyDeclarationSyntax syntax)
        {
            if (syntax.ExpressionBody?.Expression is { } expression)
            {
                return expression;
            }

            var getter = syntax.AccessorList?.Accessors.FirstOrDefault(accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody?.Expression is { } expressionBody)
            {
                return expressionBody;
            }

            return getter?.Body?.Statements.Count == 1 &&
                getter.Body.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression }
                ? returnExpression
                : null;
        }

        private bool IsPropertyDeclaredInActiveCompilation(IPropertySymbol property)
        {
            var declaration = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            return declaration is not null &&
                _model.Compilation.SyntaxTrees.Any(tree => ReferenceEquals(tree, declaration.SyntaxTree));
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (_computeMode)
            {
                if (node.Expression is null)
                {
                    return SyntaxFactory.ParseStatement("return;");
                }

                if (!_allowValueReturn || Visit(node.Expression) is not ExpressionSyntax rewrittenExpression)
                {
                    Reason ??= "Compute shader entry points cannot return a value.";
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.ParseStatement($"return {rewrittenExpression.ToFullString().Trim()};");
            }

            if (!_lowerReturns)
            {
                return base.VisitReturnStatement(node);
            }

            if (node.Expression is null)
            {
                Reason ??= "Graphics shader entry point must return its declared stage value.";
                return SyntaxFactory.EmptyStatement();
            }

            return TranslateReturnedExpression(node.Expression);
        }

        public SyntaxNode TranslateExpressionBody(ExpressionSyntax expression)
            => TranslateReturnedExpression(expression);

        private SyntaxNode TranslateReturnedExpression(ExpressionSyntax expression)
        {
            if (_stage == ShaderStage.Fragment)
            {
                var fragmentExpression = Visit(expression)?.ToFullString().Trim();
                if (string.IsNullOrWhiteSpace(fragmentExpression))
                {
                    Reason ??= "Fragment shader return expression could not be translated.";
                    return SyntaxFactory.EmptyStatement();
                }

                return SyntaxFactory.Block(
                    SyntaxFactory.ParseStatement("fragColor = " + fragmentExpression + ";"),
                    SyntaxFactory.ParseStatement("return;"));
            }

            if (_returnType is null)
            {
                Reason ??= "Vertex shader return type must be a varying payload struct.";
                return SyntaxFactory.EmptyStatement();
            }

            var assignments = new List<StatementSyntax>();
            foreach (var leaf in ShaderInterstageTraversal.Flatten(_returnType, _context))
            {
                if (!_outputFields.TryGetValue(leaf.Field, out var outputName))
                {
                    Reason ??= $"Vertex output field '{leaf.PathName}' has no stage mapping.";
                    continue;
                }

                var value = FindReturnedFieldValue(expression, leaf.Path);
                var translatedExpression = value is not null
                    ? Visit(value)?.ToFullString().Trim()
                    : GetTypeInfo(expression).Type is INamedTypeSymbol expressionType &&
                        SymbolEqualityComparer.Default.Equals(expressionType, _returnType)
                        ? _outputFields.TryGetValue(leaf.Field, out var outputFieldName)
                            ? outputFieldName
                            : _directFields.TryGetValue(leaf.Field, out var directFieldName)
                                ? directFieldName
                                : null
                        : null;
                if (string.IsNullOrWhiteSpace(translatedExpression))
                {
                    Reason ??= $"Vertex return value does not initialize field '{leaf.PathName}'.";
                    continue;
                }

                assignments.Add(SyntaxFactory.ParseStatement(outputName + " = " + translatedExpression + ";"));
            }

            assignments.Add(SyntaxFactory.ParseStatement("return;"));
            return SyntaxFactory.Block(assignments);
        }

        private ExpressionSyntax? FindReturnedFieldValue(ExpressionSyntax expression, IReadOnlyList<IFieldSymbol> path)
        {
            ExpressionSyntax current = expression;
            foreach (var field in path)
            {
                if (current is not ObjectCreationExpressionSyntax creation || creation.Initializer is not { } initializer)
                {
                    return null;
                }

                var assignment = initializer.Expressions
                    .OfType<AssignmentExpressionSyntax>()
                    .FirstOrDefault(candidate =>
                        GetSymbolInfo(candidate.Left).Symbol is IFieldSymbol assignedField &&
                        SymbolEqualityComparer.Default.Equals(assignedField, field));
                if (assignment is null)
                {
                    return null;
                }

                current = assignment.Right;
            }

            return current;
        }

        private bool TryTranslateShaderBuiltinMember(
            MemberAccessExpressionSyntax node,
            out string translated)
        {
            translated = string.Empty;
            if (GetSymbolInfo(node).Symbol is IPropertySymbol property &&
                _context.Intrinsics.TryGetIntrinsic(property, out var directBinding) &&
                directBinding.Category == IntrinsicCategory.Builtin)
            {
                if (!directBinding.SupportsStage(_stage))
                {
                    Reason ??= $"Shader builtin '{property.Name}' is not valid in {_stage} stage.";
                    return false;
                }

                translated = directBinding.GlslName;
                UsesBuiltin = true;
                return true;
            }

            if (node.Expression is not MemberAccessExpressionSyntax parent ||
                GetSymbolInfo(parent).Symbol is not IPropertySymbol parentProperty ||
                !_context.Intrinsics.TryGetIntrinsic(parentProperty, out var parentBinding) ||
                parentBinding.Category != IntrinsicCategory.Builtin)
            {
                return false;
            }

            if (!parentBinding.SupportsStage(_stage))
            {
                Reason ??= $"Shader builtin '{parentProperty.Name}' is not valid in {_stage} stage.";
                return false;
            }

            var component = node.Name.Identifier.ValueText switch
            {
                "X" or "x" => "x",
                "Y" or "y" => "y",
                "Z" or "z" => "z",
                "W" or "w" => "w",
                _ => string.Empty
            };
            if (component.Length == 0)
            {
                Reason ??= $"Unsupported component '{node.Name.Identifier.ValueText}' on shader builtin '{parentProperty.Name}'.";
                return false;
            }

            translated = parentBinding.GlslName + "." + component;
            return true;
        }

        public override SyntaxNode? VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            var isOutDeclaration = node.Parent is ArgumentSyntax argument &&
                argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
            if ((!_computeMode && !isOutDeclaration) || node.Designation is not SingleVariableDesignationSyntax designation)
            {
                return base.VisitDeclarationExpression(node);
            }

            if (GetDeclaredSymbol(designation) is not ILocalSymbol local)
            {
                Reason ??= "Compute shader out-variable declaration has no Roslyn symbol.";
                return SyntaxFactory.ParseExpression("0");
            }

            if (!TryMap(local.Type, out _))
            {
                Reason ??= "Compute shader out-variable declaration has an unsupported shader type.";
                return SyntaxFactory.ParseExpression("0");
            }

            var localName = CreateLocalName(local.Name);
            _locals[local] = localName;
            return SyntaxFactory.ParseExpression(localName);
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.ModuloExpression))
            {
                return base.VisitBinaryExpression(node);
            }

            var symbol = GetSymbolInfo(node).Symbol as IMethodSymbol;
            IntrinsicBinding? binding = null;
            var hasIntrinsicBinding = symbol is not null &&
                _context.Intrinsics.TryGetIntrinsic(symbol, out binding) &&
                string.Equals(binding.GlslName, "%", StringComparison.Ordinal);
            if (!hasIntrinsicBinding && !IsIntegerRemainder(node))
            {
                return base.VisitBinaryExpression(node);
            }

            if (hasIntrinsicBinding && !IsIntegerRemainder(binding!))
            {
                Reason ??= "C# remainder is supported only for integer shader types.";
                return base.VisitBinaryExpression(node);
            }

            if (hasIntrinsicBinding && !binding!.SupportsStage(_stage))
            {
                Reason ??= $"Intrinsic '{symbol!.Name}' is not valid in {_stage} stage.";
                return base.VisitBinaryExpression(node);
            }

            if (Visit(node.Left) is not ExpressionSyntax left ||
                Visit(node.Right) is not ExpressionSyntax right)
            {
                Reason ??= "Integer remainder operands could not be translated.";
                return base.VisitBinaryExpression(node);
            }

            var leftText = left.ToFullString();
            var rightText = right.ToFullString();
            return SyntaxFactory.ParseExpression(
                $"({leftText} - ({leftText} / {rightText}) * {rightText})");
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (!ValidateOutArguments(node, symbol))
            {
                return base.VisitInvocationExpression(node);
            }

            var args = node.ArgumentList.Arguments
                .Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node."))
                .ToArray();
            var receiver = symbol is { IsStatic: false } && node.Expression is MemberAccessExpressionSyntax memberAccess
                ? Visit(memberAccess.Expression)?.ToFullString()
                : null;
            if (symbol is not null && receiver is not null &&
                (_helperReceivers.TryGetValue(symbol, out var hasReceiver) ||
                 _helperReceivers.TryGetValue(symbol.OriginalDefinition, out hasReceiver)) &&
                !hasReceiver)
            {
                receiver = null;
            }
            var glslArguments = receiver is null
                ? args.Select(argument => argument.ToFullString())
                : new[] { receiver }.Concat(args.Select(argument => argument.ToFullString()));
            if (symbol is not null && _context.Intrinsics.TryGetIntrinsic(symbol, out var binding))
            {
                if (!binding.SupportsStage(_stage))
                {
                    Reason ??= $"Intrinsic '{symbol.Name}' is not valid in {_stage} stage.";
                }
                if (binding.GlslName is "*" or "/" or "+" or "-" or "%")
                {
                    var operatorExpression = CreateOperatorExpression(binding, glslArguments);
                    if (operatorExpression is null)
                    {
                        Reason ??= binding.GlslName == "%"
                            ? "C# remainder is supported only for integer shader types."
                            : $"Operator intrinsic '{symbol.Name}' has an unsupported arity.";
                        return base.VisitInvocationExpression(node);
                    }

                    return SyntaxFactory.ParseExpression(operatorExpression);
                }

                if (string.Equals(binding.GlslName, "discard", StringComparison.Ordinal))
                {
                    return SyntaxFactory.ParseExpression("discard");
                }

                var intrinsicArguments = GetIntrinsicArguments(binding, glslArguments);
                var glslName = string.Equals(binding.GlslName, "round", StringComparison.Ordinal)
                    ? "roundEven"
                    : binding.GlslName;
                return SyntaxFactory.ParseExpression(glslName + "(" + string.Join(", ", intrinsicArguments) + ")");
            }
            if (symbol is not null &&
                (_helperNames.TryGetValue(symbol, out var helperName) ||
                 _helperNames.TryGetValue(symbol.OriginalDefinition, out helperName)))
            {
                return SyntaxFactory.ParseExpression(helperName + "(" + string.Join(", ", glslArguments) + ")");
            }
            Reason ??= "Unsupported method call in shader body.";
            return base.VisitInvocationExpression(node);
        }

        private bool ValidateOutArguments(InvocationExpressionSyntax node, IMethodSymbol? method)
        {
            var arguments = node.ArgumentList.Arguments;
            if (!arguments.Any(argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)))
            {
                return true;
            }

            if (method is null)
            {
                Reason ??= "Shader calls with ref or out arguments must resolve to a method symbol.";
                return false;
            }

            var targets = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (argument.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    continue;
                }

                if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                {
                    Reason ??= $"Shader helper '{method.Name}' supports out arguments but not ref or in arguments.";
                    return false;
                }

                if (index >= method.Parameters.Length || method.Parameters[index].RefKind != RefKind.Out)
                {
                    Reason ??= $"Shader call '{method.Name}' has an out argument that does not match its method signature.";
                    return false;
                }

                var target = argument.Expression switch
                {
                    DeclarationExpressionSyntax declaration when declaration.Designation is SingleVariableDesignationSyntax designation
                        => GetDeclaredSymbol(designation),
                    IdentifierNameSyntax identifier => GetSymbolInfo(identifier).Symbol,
                    _ => null
                };
                if (target is not ISymbol targetSymbol ||
                    targetSymbol is not ILocalSymbol && targetSymbol is not IParameterSymbol)
                {
                    Reason ??= $"Shader out argument for '{method.Name}' must be a local or shader parameter.";
                    return false;
                }

                if (!targets.Add(targetSymbol))
                {
                    Reason ??= $"Shader call '{method.Name}' aliases multiple out arguments to '{targetSymbol.Name}'.";
                    return false;
                }
            }

            return true;
        }

        private static string? CreateOperatorExpression(
            IntrinsicBinding binding,
            IEnumerable<string> arguments)
        {
            var values = arguments.ToArray();
            var operatorToken = binding.GlslName;
            if (values.Length == 2)
            {
                if (operatorToken == "%" && !IsIntegerRemainder(binding))
                {
                    return null;
                }

                if (binding.ParameterGlslTypes is { Count: 2 } parameterTypes)
                {
                    if (IsVectorType(parameterTypes[0]) && IsScalarType(parameterTypes[1]))
                    {
                        values[1] = parameterTypes[0] + "(" + values[1] + ")";
                    }
                    else if (IsScalarType(parameterTypes[0]) && IsVectorType(parameterTypes[1]))
                    {
                        values[0] = parameterTypes[1] + "(" + values[0] + ")";
                    }
                }

                if (operatorToken == "%")
                {
                    return "(" + values[0] + " - (" + values[0] + " / " + values[1] + ") * " + values[1] + ")";
                }

                return "(" + values[0] + " " + operatorToken + " " + values[1] + ")";
            }

            if (values.Length == 1 && (operatorToken == "+" || operatorToken == "-"))
            {
                return "(" + operatorToken + values[0] + ")";
            }

            return null;
        }

        private static IReadOnlyList<string> GetIntrinsicArguments(
            IntrinsicBinding binding,
            IEnumerable<string> arguments)
        {
            var values = arguments.ToArray();
            if (!string.Equals(binding.GlslName, "atan", StringComparison.Ordinal)
                || binding.ParameterGlslTypes is not { Count: 2 } parameterTypes
                || values.Length != 2)
            {
                return values;
            }

            if (IsVectorType(parameterTypes[0]) && parameterTypes[1] == "float")
            {
                values[1] = parameterTypes[0] + "(" + values[1] + ")";
            }
            else if (parameterTypes[0] == "float" && IsVectorType(parameterTypes[1]))
            {
                values[0] = parameterTypes[1] + "(" + values[0] + ")";
            }

            return values;
        }

        private static bool IsVectorType(string? glslType)
        {
            return glslType is "vec2" or "vec3" or "vec4"
                or "ivec2" or "ivec3" or "ivec4"
                or "uvec2" or "uvec3" or "uvec4";
        }

        private static bool IsScalarType(string? glslType)
            => glslType is "float" or "int" or "uint";

        private static bool IsIntegerRemainder(IntrinsicBinding binding)
        {
            return string.Equals(binding.GlslName, "%", StringComparison.Ordinal)
                && binding.ParameterGlslTypes is { Count: 2 } parameterTypes
                && parameterTypes.All(IsIntegerType);
        }

        private bool IsIntegerRemainder(BinaryExpressionSyntax node)
        {
            var leftType = GetTypeInfo(node.Left).ConvertedType ?? GetTypeInfo(node.Left).Type;
            var rightType = GetTypeInfo(node.Right).ConvertedType ?? GetTypeInfo(node.Right).Type;
            return leftType is not null && rightType is not null &&
                TryMap(leftType, out var leftGlslType) &&
                TryMap(rightType, out var rightGlslType) &&
                IsIntegerType(leftGlslType) && IsIntegerType(rightGlslType);
        }

        private static bool IsIntegerType(string? glslType)
            => glslType is "int" or "uint"
                or "ivec2" or "ivec3" or "ivec4"
                or "uvec2" or "uvec3" or "uvec4";

        private ExpressionSyntax? RebindExpressionToActiveCompilation(ExpressionSyntax expression)
        {
            var syntaxTree = expression.SyntaxTree;
            if (syntaxTree is null)
            {
                return null;
            }

            if (_model.Compilation.SyntaxTrees.Any(tree => ReferenceEquals(tree, syntaxTree)))
            {
                return expression;
            }

            var sourceText = syntaxTree.GetText().ToString();
            foreach (var candidateTree in _model.Compilation.SyntaxTrees)
            {
                if (!string.Equals(candidateTree.FilePath, syntaxTree.FilePath, StringComparison.Ordinal) ||
                    !string.Equals(candidateTree.GetText().ToString(), sourceText, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidateNode = candidateTree.GetRoot().FindNode(expression.Span, getInnermostNodeForTie: true);
                if (candidateNode is ExpressionSyntax candidateExpression)
                {
                    return candidateExpression;
                }
            }

            return null;
        }

        private SemanticModel? TryGetSemanticModel(SyntaxNode node)
        {
            var syntaxTree = node.SyntaxTree;
            if (ReferenceEquals(syntaxTree, _model.SyntaxTree))
            {
                return _model;
            }

            if (syntaxTree is null || !_model.Compilation.SyntaxTrees.Any(tree => ReferenceEquals(tree, syntaxTree)))
            {
                Reason ??= "Shader helper syntax is not part of the active Roslyn compilation.";
                return null;
            }

            return _model.Compilation.GetSemanticModel(syntaxTree);
        }

        private bool TryGetTypeInfo(ExpressionSyntax expression, out TypeInfo typeInfo)
        {
            var semanticModel = TryGetSemanticModel(expression);
            if (semanticModel is null)
            {
                typeInfo = default;
                return false;
            }

            typeInfo = semanticModel.GetTypeInfo(expression);
            return true;
        }

        private SymbolInfo GetSymbolInfo(ExpressionSyntax expression)
        {
            return TryGetSemanticModel(expression)?.GetSymbolInfo(expression) ?? default;
        }

        private TypeInfo GetTypeInfo(ExpressionSyntax expression)
        {
            return TryGetSemanticModel(expression)?.GetTypeInfo(expression) ?? default;
        }

        private TypeInfo GetTypeInfo(TypeSyntax type)
        {
            return TryGetSemanticModel(type)?.GetTypeInfo(type) ?? default;
        }

        private ISymbol? GetDeclaredSymbol(VariableDeclaratorSyntax declaration)
        {
            return TryGetSemanticModel(declaration)?.GetDeclaredSymbol(declaration);
        }

        private ISymbol? GetDeclaredSymbol(SingleVariableDesignationSyntax designation)
        {
            return TryGetSemanticModel(designation)?.GetDeclaredSymbol(designation);
        }

        private ISymbol? GetDeclaredSymbol(ForEachStatementSyntax statement)
        {
            return TryGetSemanticModel(statement)?.GetDeclaredSymbol(statement);
        }

        private static bool IsSupportedAssignmentOperator(SyntaxKind kind)
            => kind is SyntaxKind.SimpleAssignmentExpression or
                SyntaxKind.AddAssignmentExpression or
                SyntaxKind.SubtractAssignmentExpression or
                SyntaxKind.MultiplyAssignmentExpression or
                SyntaxKind.DivideAssignmentExpression or
                SyntaxKind.ModuloAssignmentExpression;

        private bool HasConstantValue(ExpressionSyntax expression)
        {
            return TryGetSemanticModel(expression)?.GetConstantValue(expression).HasValue == true;
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (!TryGetTypeInfo(node, out var typeInfo))
            {
                return node;
            }

            return TryTranslateObjectCreation(typeInfo.Type, node.ArgumentList, node.Initializer) ??
                base.VisitObjectCreationExpression(node);
        }

        public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            if (!TryGetTypeInfo(node, out var typeInfo))
            {
                return node;
            }

            return TryTranslateObjectCreation(typeInfo.ConvertedType ?? typeInfo.Type, node.ArgumentList, node.Initializer) ??
                SetUnsupportedImplicitCreationReason(node);
        }

        private SyntaxNode SetUnsupportedImplicitCreationReason(ImplicitObjectCreationExpressionSyntax node)
        {
            Reason ??= "Target-typed shader constructor has an unsupported type.";
            return base.VisitImplicitObjectCreationExpression(node) ?? node;
        }

        private ExpressionSyntax? TryTranslateObjectCreation(
            ITypeSymbol? type,
            ArgumentListSyntax? arguments,
            InitializerExpressionSyntax? initializer)
        {
            if (type is INamedTypeSymbol structType &&
                TryTranslateStructCreation(structType, arguments, initializer, out var structExpression))
            {
                return structExpression;
            }

            if (type is null || !TryMap(type, out var glslType))
            {
                return null;
            }

            var translatedArguments = arguments?.Arguments
                .Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node."))
                .ToArray() ?? Array.Empty<ExpressionSyntax>();
            return SyntaxFactory.ParseExpression(
                glslType + "(" + string.Join(", ", translatedArguments.Select(argument => argument.ToFullString())) + ")");
        }

        private bool TryTranslateStructCreation(
            INamedTypeSymbol type,
            ArgumentListSyntax? arguments,
            InitializerExpressionSyntax? initializer,
            out ExpressionSyntax translated)
        {
            translated = SyntaxFactory.ParseExpression("0");
            if (!_structNames.TryGetValue(type, out var structName))
            {
                return false;
            }

            var members = GetStructValueMembers(type).ToArray();
            var values = new string[members.Length];
            for (var index = 0; index < members.Length; index++)
            {
                var memberType = members[index] is IFieldSymbol field ? field.Type : ((IPropertySymbol)members[index]).Type;
                if (!TryCreateZeroValue(memberType, out var memberZero))
                {
                    translated = SyntaxFactory.ParseExpression("0");
                    return true;
                }

                values[index] = memberZero.ToFullString();
            }

            if (arguments is { Arguments.Count: > 0 })
            {
                var constructor = arguments.Parent is ExpressionSyntax creation
                    ? GetSymbolInfo(creation).Symbol as IMethodSymbol
                    : null;
                var isFieldwiseConstructor = constructor is not null &&
                    constructor.Parameters.Length == members.Length &&
                    arguments.Arguments.Count == members.Length &&
                    constructor.Parameters.Select(parameter => parameter.Type).Zip(
                        members.Select(member => member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type),
                        SymbolEqualityComparer.Default.Equals).All(pair => pair);
                if (isFieldwiseConstructor)
                {
                    for (var index = 0; index < arguments.Arguments.Count; index++)
                    {
                        if (arguments.Arguments[index].NameColon is not null ||
                            arguments.Arguments[index].RefKindKeyword.RawKind != 0 ||
                            Visit(arguments.Arguments[index].Expression) is not ExpressionSyntax value)
                        {
                            Reason ??= $"Shader struct '{type.Name}' constructors support only positional value arguments.";
                            return true;
                        }

                        values[index] = value.ToFullString().Trim();
                    }
                }
                else if (constructor is null ||
                         constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ConstructorDeclarationSyntax constructorSyntax ||
                         !TryTranslateValueConstructor(constructor, constructorSyntax, arguments, members, values))
                {
                    Reason ??= $"Shader struct '{type.Name}' constructors must be positional fieldwise value constructors or assign value members directly.";
                    return true;
                }
            }

            if (initializer is not null)
            {
                foreach (var expression in initializer.Expressions)
                {
                    if (expression is not AssignmentExpressionSyntax assignment ||
                        GetSymbolInfo(assignment.Left).Symbol is not ISymbol assignedMember)
                    {
                        Reason ??= $"Shader struct '{type.Name}' initializers must contain named value assignments.";
                        return true;
                    }

                    var memberIndex = Array.FindIndex(members, member =>
                        SymbolEqualityComparer.Default.Equals(member, assignedMember));
                    if (memberIndex < 0 || Visit(assignment.Right) is not ExpressionSyntax value)
                    {
                        Reason ??= $"Shader struct '{type.Name}' initializer targets an unsupported member.";
                        return true;
                    }

                    values[memberIndex] = value.ToFullString().Trim();
                }
            }

            translated = SyntaxFactory.ParseExpression(structName + "(" + string.Join(", ", values) + ")");
            return true;
        }

        private bool TryTranslateValueConstructor(
            IMethodSymbol constructor,
            ConstructorDeclarationSyntax syntax,
            ArgumentListSyntax arguments,
            IReadOnlyList<ISymbol> members,
            string[] values)
        {
            if (arguments.Arguments.Count != constructor.Parameters.Length ||
                syntax.Initializer is { ArgumentList.Arguments.Count: > 0 })
            {
                return false;
            }

            var argumentValues = new string[arguments.Arguments.Count];
            for (var index = 0; index < arguments.Arguments.Count; index++)
            {
                var argument = arguments.Arguments[index];
                if (argument.NameColon is not null || argument.RefKindKeyword.RawKind != 0 ||
                    Visit(argument.Expression) is not ExpressionSyntax value)
                {
                    return false;
                }

                argumentValues[index] = value.ToFullString().Trim();
            }

            var previousValues = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            try
            {
                for (var index = 0; index < constructor.Parameters.Length; index++)
                {
                    var parameter = constructor.Parameters[index];
                    if (_valueSubstitutions.TryGetValue(parameter, out var previous))
                    {
                        previousValues[parameter] = previous;
                    }

                    _valueSubstitutions[parameter] = argumentValues[index];
                    _valueSubstitutions[parameter.OriginalDefinition] = argumentValues[index];
                }

                if (syntax.ExpressionBody?.Expression is { } expressionBody)
                {
                    if (expressionBody is not AssignmentExpressionSyntax assignment ||
                        !TryApplyValueConstructorAssignment(assignment, members, values))
                    {
                        return false;
                    }
                }
                else
                {
                    foreach (var statement in syntax.Body?.Statements ?? [])
                    {
                        if (statement is not ExpressionStatementSyntax
                            {
                                Expression: AssignmentExpressionSyntax assignment
                            } || !TryApplyValueConstructorAssignment(assignment, members, values))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            finally
            {
                foreach (var parameter in constructor.Parameters)
                {
                    _valueSubstitutions.Remove(parameter);
                    _valueSubstitutions.Remove(parameter.OriginalDefinition);
                    if (previousValues.TryGetValue(parameter, out var previous))
                    {
                        _valueSubstitutions[parameter] = previous;
                    }
                }
            }
        }

        private bool TryApplyValueConstructorAssignment(
            AssignmentExpressionSyntax assignment,
            IReadOnlyList<ISymbol> members,
            string[] values)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                GetSymbolInfo(assignment.Left).Symbol is not ISymbol assignedMember)
            {
                return false;
            }

            var memberIndex = -1;
            for (var index = 0; index < members.Count; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(members[index], assignedMember))
                {
                    memberIndex = index;
                    break;
                }
            }

            if (memberIndex < 0 || Visit(assignment.Right) is not ExpressionSyntax value)
            {
                return false;
            }

            values[memberIndex] = value.ToFullString().Trim();
            return true;
        }

        private bool TryCreateZeroValue(ITypeSymbol type, out ExpressionSyntax zero)
            => TryCreateZeroValue(type, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out zero);

        private bool TryCreateZeroValue(
            ITypeSymbol type,
            HashSet<INamedTypeSymbol> visiting,
            out ExpressionSyntax zero)
        {
            if (type is INamedTypeSymbol structType && _structNames.TryGetValue(structType, out var structName))
            {
                if (!visiting.Add(structType))
                {
                    Reason ??= $"Recursive shader struct '{structType.Name}' cannot receive a default value.";
                    zero = SyntaxFactory.ParseExpression("0");
                    return false;
                }

                try
                {
                    var values = new List<string>();
                    foreach (var member in GetStructValueMembers(structType))
                    {
                        var memberType = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
                        if (!TryCreateZeroValue(memberType, visiting, out var memberZero))
                        {
                            zero = SyntaxFactory.ParseExpression("0");
                            return false;
                        }

                        values.Add(memberZero.ToFullString());
                    }

                    zero = SyntaxFactory.ParseExpression(structName + "(" + string.Join(", ", values) + ")");
                    return true;
                }
                finally
                {
                    visiting.Remove(structType);
                }
            }

            if (TryMap(type, out var glslType))
            {
                var scalarZero = glslType == "bool" || glslType.StartsWith("bvec", StringComparison.Ordinal)
                    ? "false"
                    : glslType is "int" or "uint" || glslType.StartsWith("ivec", StringComparison.Ordinal) ||
                      glslType.StartsWith("uvec", StringComparison.Ordinal)
                        ? "0"
                        : "0.0";
                zero = SyntaxFactory.ParseExpression(glslType + "(" + scalarZero + ")");
                return true;
            }

            Reason ??= $"Shader type '{type}' has no zero-value lowering.";
            zero = SyntaxFactory.ParseExpression("0");
            return false;
        }

        private static IEnumerable<ISymbol> GetStructValueMembers(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared)
                {
                    yield return field;
                }
                else if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer &&
                         property.Parameters.Length == 0 && property.GetMethod is not null &&
                         ShaderStructSupport.IsAutoProperty(property))
                {
                    yield return property;
                }
            }
        }

        public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
        {
            var targetType = GetTypeInfo(node.Type).Type;
            if (targetType is not null && TryMap(targetType, out var glslType))
            {
                var operand = Visit(node.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no cast operand.");
                return SyntaxFactory.ParseExpression(glslType + "(" + operand.ToFullString() + ")");
            }

            Reason ??= "Shader cast has an unsupported target type.";
            return base.VisitCastExpression(node);
        }

        public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var rewritten = base.VisitVariableDeclaration(node) as VariableDeclarationSyntax;
            if (rewritten is null)
            {
                return null;
            }

            if (node.Variables.Count == 1)
            {
                var type = node.Type.IsVar
                    ? node.Variables[0].Initializer is { } initializer
                        ? GetTypeInfo(initializer.Value).Type
                        : null
                    : GetTypeInfo(node.Type).Type;
                if (_computeMode && type is INamedTypeSymbol statelessType &&
                    ShaderStructSupport.IsStateless(statelessType) &&
                    !TryMap(statelessType, out _))
                {
                    return SyntaxFactory.EmptyStatement();
                }
                if (type is INamedTypeSymbol namedType && _structNames.TryGetValue(namedType, out var structName))
                {
                    return rewritten.WithType(SyntaxFactory.ParseTypeName(structName));
                }

                if (type is not null && TryMap(type, out var glslType))
                {
                    return rewritten.WithType(SyntaxFactory.ParseTypeName(glslType));
                }
            }

            if (!_computeMode &&
                node.Variables.Count == 1 &&
                GetDeclaredSymbol(node.Variables[0]) is ILocalSymbol local)
            {
                var localName = CreateLocalName(local.Name);
                _locals[local] = localName;
                var variable = rewritten.Variables[0].WithIdentifier(SyntaxFactory.Identifier(localName));
                rewritten = rewritten.WithVariables(SyntaxFactory.SingletonSeparatedList(variable));
            }

            return rewritten;
        }

        private bool TryMap(ITypeSymbol type, out string glslType)
        {
            if (ShaderSemanticTypeSupport.TryMapType(type, _context, out glslType))
            {
                return true;
            }

            if (_context.Intrinsics.TryMapType(type, out glslType))
            {
                return true;
            }
            if (type is INamedTypeSymbol halfType &&
                halfType.Name == "half" &&
                halfType.ContainingNamespace.ToDisplayString() == "Delta")
            {
                glslType = "float16_t";
                return true;
            }
            if (type is INamedTypeSymbol namedType && _structNames.TryGetValue(namedType, out glslType))
            {
                return true;
            }
            if (ShaderEnumSupport.TryMap(type, out glslType))
            {
                return true;
            }
            glslType = type.SpecialType switch
            {
                SpecialType.System_Boolean => "bool",
                SpecialType.System_Single => "float",
                SpecialType.System_UInt32 => "uint",
                SpecialType.System_Int32 => "int",
                SpecialType.System_Double => "double",
                _ => string.Empty
            };
            return glslType.Length > 0;
        }

        private string CreateLocalName(string name)
        {
            var baseName = "local_" + Sanitize(name);
            var candidate = baseName;
            var suffix = 2;
            while (_locals.Values.Contains(candidate, StringComparer.Ordinal))
            {
                candidate = baseName + "_" + suffix++;
            }

            return candidate;
        }

        private bool IsComputeStorageTarget(ExpressionSyntax expression)
        {
            var symbol = GetSymbolInfo(expression).Symbol;
            return symbol is IFieldSymbol field && _storageFields.ContainsKey(field) ||
                   symbol is IParameterSymbol parameter && _storageParameters.Contains(parameter);
        }

        private static bool IsComputeLocalAssignment(AssignmentExpressionSyntax assignment)
            => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
               assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
               assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) ||
               assignment.IsKind(SyntaxKind.MultiplyAssignmentExpression) ||
               assignment.IsKind(SyntaxKind.DivideAssignmentExpression);
    }
}
