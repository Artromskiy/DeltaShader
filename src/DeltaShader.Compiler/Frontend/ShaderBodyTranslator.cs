using System;
using System.Collections.Generic;
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
        bool allowValueReturn = false)
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
            allowValueReturn);
        var rewritten = methodSyntax.Body is { } body
            ? rewriter.Visit(body)
            : methodSyntax.ExpressionBody?.Expression is { } expression
                ? rewriter.Visit(expression)
                : null;

        if (rewritten is BlockSyntax block)
        {
            translated = string.Join("\n", block.Statements.Select(statement => statement.ToFullString().Trim()));
        }
        else
        {
            translated = rewritten?.ToFullString().Trim() ?? string.Empty;
        }

        translated = NormalizeComputeText(translated);
        usesBuiltin = rewriter.UsesBuiltin;
        reason = rewriter.Reason;
        return reason is null;
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
        out string? reason)
    {
        var rewriter = CreateComputeRewriter(model, context, contextParameter, resourceBindings,
            parameterMap, locals, helperNames);
        var rewritten = rewriter.Visit(expression);
        translated = NormalizeComputeText(rewritten?.ToFullString().Trim() ?? string.Empty);
        usesBuiltin = rewriter.UsesBuiltin;
        reason = rewriter.Reason;
        return reason is null;
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
        bool allowValueReturn = false)
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
            allowValueReturn);
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
        bool lowerReturns = false)
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
            lowerReturns);
        var rewritten = body is ExpressionSyntax expression && lowerReturns
            ? rewriter.TranslateExpressionBody(expression)
            : rewriter.Visit(body);
        translated = rewritten?.ToFullString().Trim() ?? string.Empty;
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
        translated = translated.Replace(";", ";\n").Replace("\r\n", "\n").Replace("\r", "\n");
        translated = Regex.Replace(translated, @"\b(vec[234]|ivec[234]|uvec[234]|bvec[234]|mat[234]|float|int|uint|bool)([A-Za-z_]\w*)\s*=", "$1 $2 =", RegexOptions.None);
        foreach (var structName in structNames.Values)
        {
            translated = Regex.Replace(translated, $@"\b({Regex.Escape(structName)})([A-Za-z_]\w*)\s*=", "$1 $2 =", RegexOptions.None);
        }
        translated = System.Text.RegularExpressions.Regex.Replace(translated, @"(?<=\d)f\b", string.Empty);
        reason = rewriter.Reason;
        return reason is null;
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
        if (binding.GlslName is "*" or "/" or "+" or "-")
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
        public string? Reason { get; private set; }
        public bool UsesBuiltin { get; private set; }

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
            bool allowValueReturn = false)
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
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is ILocalSymbol local && _locals.TryGetValue(local, out var localName))
            {
                return SyntaxFactory.ParseName(localName);
            }

            if (symbol is IParameterSymbol parameter && _parameters.TryGetValue(parameter, out var parameterName))
            {
                return SyntaxFactory.ParseName(parameterName);
            }
            return base.VisitIdentifierName(node);
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
                if (statement is not BlockSyntax and not LocalDeclarationStatementSyntax and not IfStatementSyntax and not ForStatementSyntax and not ExpressionStatementSyntax and not ReturnStatementSyntax)
                {
                    Reason ??= "Only declarations, conditionals, for loops, and assignments are supported in compute shader bodies.";
                    continue;
                }

                if (Visit(statement) is StatementSyntax rewritten)
                {
                    statements.Add(rewritten);
                }
            }

            return SyntaxFactory.Block(statements);
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (!_computeMode)
            {
                return base.VisitLocalDeclarationStatement(node);
            }

            return TranslateLocalDeclaration(node.Declaration);
        }

        private StatementSyntax? TranslateLocalDeclaration(VariableDeclarationSyntax declaration)
        {
            if (declaration.Variables.Count != 1 || declaration.Variables[0].Initializer is not { } initializer)
            {
                Reason ??= "Local declarations require exactly one initialized variable in a compute shader body.";
                return null;
            }

            var variable = declaration.Variables[0];
            if (_model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
            {
                Reason ??= "Compute shader local declaration has no Roslyn symbol.";
                return null;
            }

            var type = declaration.Type.IsVar
                ? _model.GetTypeInfo(initializer.Value).ConvertedType ?? _model.GetTypeInfo(initializer.Value).Type
                : _model.GetTypeInfo(declaration.Type).Type;
            if (type is null || !TryMap(type, out var glslType))
            {
                Reason ??= "Local declaration has an unsupported shader type.";
                return null;
            }

            if (Visit(initializer.Value) is not ExpressionSyntax rewrittenInitializer)
            {
                Reason ??= "Compute shader local initializer could not be translated.";
                return null;
            }

            var localName = CreateLocalName(local.Name);
            _locals[local] = localName;
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
                _model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
                _locals.ContainsKey(local);

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (!_computeMode)
            {
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
                _model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
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
                _model.GetSymbolInfo(outputIdentifier).Symbol is IParameterSymbol outputParameter &&
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
                var typeInfo = _model.GetTypeInfo(node);
                var type = typeInfo.ConvertedType ?? typeInfo.Type;
                if (type is not null && TryMap(type, out var glslType))
                {
                    var zero = glslType == "bool" ? "false" : glslType is "int" or "uint" ? "0" : "0.0";
                    return SyntaxFactory.ParseExpression(glslType + "(" + zero + ")");
                }
            }

            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (TryTranslateShaderBuiltinMember(node, out var builtinExpression))
            {
                return SyntaxFactory.ParseExpression(builtinExpression);
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
                return SyntaxFactory.ParseExpression(receiver + "." + structFieldName);
            }
            if (symbol is IPropertySymbol structProperty && _structProperties.TryGetValue(structProperty, out var structPropertyName))
            {
                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                return SyntaxFactory.ParseExpression(receiver + "." + structPropertyName);
            }

            if (node.Name.Identifier.ValueText == "Length" &&
                _model.GetSymbolInfo(node.Expression).Symbol is IFieldSymbol resourceField &&
                _storageFields.TryGetValue(resourceField, out var resourceName))
            {
                return SyntaxFactory.ParseExpression(resourceName + ".data.length()");
            }

            return base.VisitMemberAccessExpression(node);
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
            foreach (var field in _returnType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
            {
                if (!_outputFields.TryGetValue(field, out var outputName))
                {
                    Reason ??= $"Vertex output field '{field.Name}' has no stage mapping.";
                    continue;
                }

                var value = FindReturnedFieldValue(expression, field);
                var translatedExpression = value is not null
                    ? Visit(value)?.ToFullString().Trim()
                    : _model.GetTypeInfo(expression).Type is INamedTypeSymbol expressionType &&
                        SymbolEqualityComparer.Default.Equals(expressionType, _returnType) &&
                        _directFields.TryGetValue(field, out var directFieldName)
                        ? directFieldName
                        : null;
                if (string.IsNullOrWhiteSpace(translatedExpression))
                {
                    Reason ??= $"Vertex return value does not initialize field '{field.Name}'.";
                    continue;
                }

                assignments.Add(SyntaxFactory.ParseStatement(outputName + " = " + translatedExpression + ";"));
            }

            assignments.Add(SyntaxFactory.ParseStatement("return;"));
            return SyntaxFactory.Block(assignments);
        }

        private ExpressionSyntax? FindReturnedFieldValue(ExpressionSyntax expression, IFieldSymbol field)
        {
            if (expression is ObjectCreationExpressionSyntax creation && creation.Initializer is { } initializer)
            {
                return initializer.Expressions
                    .OfType<AssignmentExpressionSyntax>()
                    .FirstOrDefault(assignment => string.Equals(assignment.Left.ToString(), field.Name, StringComparison.Ordinal))?.Right;
            }

            return null;
        }

        private bool TryTranslateShaderBuiltinMember(
            MemberAccessExpressionSyntax node,
            out string translated)
        {
            translated = string.Empty;
            if (_model.GetSymbolInfo(node).Symbol is IPropertySymbol property &&
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
                _model.GetSymbolInfo(parent).Symbol is not IPropertySymbol parentProperty ||
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
            if (!_computeMode || node.Designation is not SingleVariableDesignationSyntax designation)
            {
                return base.VisitDeclarationExpression(node);
            }

            if (_model.GetDeclaredSymbol(designation) is not ILocalSymbol local)
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

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var args = node.ArgumentList.Arguments.Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node.")).ToArray();
            var receiver = symbol is { IsStatic: false } && node.Expression is MemberAccessExpressionSyntax memberAccess
                ? Visit(memberAccess.Expression)?.ToFullString()
                : null;
            var glslArguments = receiver is null
                ? args.Select(argument => argument.ToFullString())
                : new[] { receiver }.Concat(args.Select(argument => argument.ToFullString()));
            if (symbol is not null && _context.Intrinsics.TryGetIntrinsic(symbol, out var binding))
            {
                if (!binding.SupportsStage(_stage))
                {
                    Reason ??= $"Intrinsic '{symbol.Name}' is not valid in {_stage} stage.";
                }
                if (binding.GlslName is "*" or "/" or "+" or "-")
                {
                    return base.VisitInvocationExpression(node);
                }
                return SyntaxFactory.ParseExpression(binding.GlslName + "(" + string.Join(", ", glslArguments) + ")");
            }
            if (symbol is not null && _helperNames.TryGetValue(symbol.OriginalDefinition, out var helperName))
            {
                return SyntaxFactory.ParseExpression(helperName + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            Reason ??= "Unsupported method call in shader body.";
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var type = _model.GetTypeInfo(node).Type;
            if (type is INamedTypeSymbol structType &&
                TryTranslateStructCreation(structType, node.Initializer, out var structExpression))
            {
                return structExpression;
            }

            if (type is not null && _context.Intrinsics.TryMapType(type, out var glslType))
            {
                var args = node.ArgumentList?.Arguments.Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node.")).ToArray() ?? Array.Empty<ExpressionSyntax>();
                return SyntaxFactory.ParseExpression(glslType + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            return base.VisitObjectCreationExpression(node);
        }

        public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            var typeInfo = _model.GetTypeInfo(node);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type is INamedTypeSymbol structType &&
                TryTranslateStructCreation(structType, node.Initializer, out var structExpression))
            {
                return structExpression;
            }

            if (type is not null && _context.Intrinsics.TryMapType(type, out var glslType))
            {
                var args = node.ArgumentList.Arguments
                    .Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node."))
                    .ToArray();
                return SyntaxFactory.ParseExpression(glslType + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }

            Reason ??= "Target-typed shader constructor has an unsupported type.";
            return base.VisitImplicitObjectCreationExpression(node);
        }

        private bool TryTranslateStructCreation(
            INamedTypeSymbol type,
            InitializerExpressionSyntax? initializer,
            out ExpressionSyntax translated)
        {
            translated = SyntaxFactory.ParseExpression("0");
            if (!_structNames.TryGetValue(type, out var structName))
            {
                return false;
            }

            if (initializer is null)
            {
                Reason ??= $"Local shader struct '{type.Name}' requires an object initializer.";
                return true;
            }

            var assignments = initializer.Expressions.OfType<AssignmentExpressionSyntax>().ToArray();
            var values = new List<string>();
            foreach (var member in GetStructValueMembers(type))
            {
                var assignment = assignments.FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(candidate.Left).Symbol, member));
                if (assignment is null || Visit(assignment.Right) is not ExpressionSyntax value)
                {
                    Reason ??= $"Local shader struct '{type.Name}' does not initialize member '{member.Name}'.";
                    return true;
                }

                values.Add(value.ToFullString().Trim());
            }

            translated = SyntaxFactory.ParseExpression(structName + "(" + string.Join(", ", values) + ")");
            return true;
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
                         property.Parameters.Length == 0 && property.GetMethod is not null)
                {
                    yield return property;
                }
            }
        }

        public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
        {
            var targetType = _model.GetTypeInfo(node.Type).Type;
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

            if (node.Type.IsVar && node.Variables.Count == 1)
            {
                var type = node.Variables[0].Initializer is { } initializer
                    ? _model.GetTypeInfo(initializer.Value).Type
                    : null;
                if (type is INamedTypeSymbol namedType && _structNames.TryGetValue(namedType, out var structName))
                {
                    return rewritten.WithType(SyntaxFactory.ParseTypeName(structName));
                }

                if (type is not null && TryMap(type, out var glslType))
                {
                    return rewritten.WithType(SyntaxFactory.ParseTypeName(glslType));
                }
            }
            return rewritten;
        }

        private bool TryMap(ITypeSymbol type, out string glslType)
        {
            if (_context.Intrinsics.TryMapType(type, out glslType))
            {
                return true;
            }
            if (type is INamedTypeSymbol namedType && _structNames.TryGetValue(namedType, out glslType))
            {
                return true;
            }
            glslType = type.SpecialType switch
            {
                SpecialType.System_Boolean => "bool",
                SpecialType.System_Single => "float",
                SpecialType.System_UInt32 => "uint",
                SpecialType.System_Int32 => "int",
                _ => string.Empty
            };
            return glslType.Length > 0;
        }

        private string CreateLocalName(string name)
        {
            var baseName = Sanitize(name);
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
            var symbol = _model.GetSymbolInfo(expression).Symbol;
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
