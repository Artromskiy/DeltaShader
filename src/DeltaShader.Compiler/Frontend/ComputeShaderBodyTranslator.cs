using System.Globalization;
using Delta.Shader;
using Delta.Shader.Compiler.Intrinsics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

internal sealed class ComputeShaderBodyTranslationResult
{
    public string Body { get; init; } = string.Empty;
    public bool UsesBuiltinInvocationId { get; init; }
}

internal sealed class ComputeShaderBodyTranslator
{
    private readonly IntrinsicRegistry _intrinsics;

    public ComputeShaderBodyTranslator(IntrinsicRegistry intrinsics)
    {
        _intrinsics = intrinsics;
    }

    public bool TryTranslate(
        IMethodSymbol method,
        MethodDeclarationSyntax methodSyntax,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out ComputeShaderBodyTranslationResult? result,
        out string? reason,
        out string diagnosticId)
    {
        reason = null;
        result = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        var body = methodSyntax.Body;
        if (body is null)
        {
            reason = "Compute entry point body is required for executable slices.";
            return false;
        }

        if (body.Statements.Count == 0)
        {
            result = new ComputeShaderBodyTranslationResult
            {
                Body = string.Empty,
                UsesBuiltinInvocationId = false
            };

            return true;
        }

        if (body.Statements.Count != 1)
        {
            reason = "Only a single top-level statement is supported in executable MVP entry points.";
            return false;
        }

        var statement = body.Statements[0];
        if (statement is IfStatementSyntax ifStatement)
        {
            var statementDiagnosticId = ShaderDiagnosticId.DSH008;
            if (!TryTranslateInvocationCondition(ifStatement.Condition, semanticModel, invocationParameter, resourceBindings, out var condition, out var conditionUsesBuiltin, out reason, out statementDiagnosticId))
            {
                diagnosticId = statementDiagnosticId;
                return false;
            }

            if (!TryTranslateStoreExpression(ifStatement.Statement, semanticModel, invocationParameter, resourceBindings, out var store, out var storeUsesBuiltin, out reason, out statementDiagnosticId))
            {
                diagnosticId = statementDiagnosticId;
                return false;
            }

            result = new ComputeShaderBodyTranslationResult
            {
                UsesBuiltinInvocationId = conditionUsesBuiltin || storeUsesBuiltin,
                Body = $"if ({condition})\n    {{\n        {store}\n    }}"
            };

            return true;
        }

        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            if (TryTranslateStoreExpression(expressionStatement, semanticModel, invocationParameter, resourceBindings,
                    out var storeBody, out var usesBuiltin, out reason, out diagnosticId))
            {
                result = new ComputeShaderBodyTranslationResult
                {
                    UsesBuiltinInvocationId = usesBuiltin,
                    Body = storeBody
                };

                return true;
            }

            return false;
        }

        reason = "Unsupported compute entry point body shape.";
        return false;
    }

    private bool TryTranslateStoreExpression(
        StatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        if (statement is BlockSyntax block)
        {
            if (block.Statements.Count != 1)
            {
                reason = "Store block must contain a single expression statement.";
                return false;
            }

            statement = block.Statements[0];
        }

        if (statement is not ExpressionStatementSyntax exprStmt)
        {
            reason = "Store statement must be a single expression statement.";
            return false;
        }

        if (exprStmt.Expression is AssignmentExpressionSyntax assignment &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            assignment.Left is ElementAccessExpressionSyntax elementAccess)
        {
            if (!TryTranslateResourceTarget(elementAccess.Expression, semanticModel, resourceBindings, out var indexedBuffer, out reason))
            {
                return false;
            }

            if (elementAccess.ArgumentList.Arguments.Count != 1 ||
                !TryTranslateIndexExpression(elementAccess.ArgumentList.Arguments[0].Expression, invocationParameter, semanticModel,
                    out var indexedAt, out var indexedUsesBuiltin, out reason))
            {
                reason ??= "Indexed storage-buffer access requires exactly one index.";
                return false;
            }

            if (!TryTranslateValueExpression(assignment.Right, semanticModel, invocationParameter, resourceBindings,
                    out var indexedValue, out var indexedValueUsesBuiltin, out reason, out diagnosticId))
            {
                return false;
            }

            translated = $"{indexedBuffer}.data[{indexedAt}] = {indexedValue};";
            usesBuiltin = indexedUsesBuiltin || indexedValueUsesBuiltin;
            return true;
        }

        if (exprStmt.Expression is not InvocationExpressionSyntax invocation)
        {
            reason = "Only invocation statements are supported in executable MVP body.";
            return false;
        }

        if (!TryGetSymbol(invocation, semanticModel, out var targetMethod) || targetMethod is null)
        {
            reason = "Unable to resolve store invocation symbol.";
            return false;
        }

        if (targetMethod.Name != "Store" || targetMethod.Parameters.Length != 2)
        {
            reason = "Executable body currently supports only StorageBuffer.Store(index, value) calls.";
            return false;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            reason = "Storage buffer store must be expressed through member access.";
            return false;
        }

        if (!TryTranslateResourceTarget(access.Expression, semanticModel, resourceBindings, out var bufferName, out reason))
        {
            return false;
        }

        var indexArg = invocation.ArgumentList.Arguments[0].Expression;
        if (!TryTranslateIndexExpression(indexArg, invocationParameter, semanticModel, out var index, out var indexUsesBuiltin, out reason))
        {
            return false;
        }

        var valueArg = invocation.ArgumentList.Arguments[1].Expression;
        if (!TryTranslateValueExpression(valueArg, semanticModel, invocationParameter, resourceBindings, out var value, out var valueUsesBuiltin, out reason, out var valueDiagnosticId))
        {
            diagnosticId = valueDiagnosticId;
            return false;
        }

        translated = $"{bufferName}.data[{index}] = {value};";
        usesBuiltin = indexUsesBuiltin || valueUsesBuiltin;
        return true;
    }

    private bool TryTranslateInvocationCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out string condition,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        reason = null;
        usesBuiltin = false;
        diagnosticId = ShaderDiagnosticId.DSH008;
        condition = string.Empty;

        if (expression is not BinaryExpressionSyntax binary || binary.OperatorToken.ValueText != "<")
        {
            reason = "Only '<' bounds checks are supported for executable MVP.";
            return false;
        }

        if (!TryTranslateIndexExpression(binary.Left, invocationParameter, semanticModel, out var left, out var leftBuiltin, out reason))
        {
            return false;
        }

        if (!TryTranslateValueExpression(binary.Right, semanticModel, invocationParameter, resourceBindings, out var right, out _, out reason, out diagnosticId))
        {
            return false;
        }

        usesBuiltin = leftBuiltin;
        condition = $"{left} < {right}";
        return true;
    }

    private bool TryTranslateIndexExpression(
        ExpressionSyntax expression,
        IParameterSymbol? invocationParameter,
        SemanticModel semanticModel,
        out string translated,
        out bool usesBuiltin,
        out string? reason)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;

        if (TryTranslateValueExpression(
            expression,
            semanticModel,
            invocationParameter,
            new Dictionary<IParameterSymbol, uint>(SymbolEqualityComparer.Default),
            out translated,
            out usesBuiltin,
            out reason,
            out _))
        {
            return true;
        }

        return false;
    }

    private bool TryTranslateValueExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        switch (expression)
        {
            case ObjectCreationExpressionSyntax objectCreation
                when semanticModel.GetTypeInfo(objectCreation).Type is { } objectCreationType &&
                     _intrinsics.TryMapType(objectCreationType, out var constructorType):
                {
                    var arguments = new List<string>(objectCreation.ArgumentList?.Arguments.Count ?? 0);
                    var constructorUsesBuiltin = false;
                    foreach (var argument in objectCreation.ArgumentList?.Arguments ?? default)
                    {
                        if (!TryTranslateValueExpression(argument.Expression, semanticModel, invocationParameter, resourceBindings,
                                out var translatedArgument, out var argumentUsesBuiltin, out reason, out diagnosticId))
                        {
                            return false;
                        }

                        arguments.Add(translatedArgument);
                        constructorUsesBuiltin |= argumentUsesBuiltin;
                    }

                    translated = $"{constructorType}({string.Join(", ", arguments)})";
                    usesBuiltin = constructorUsesBuiltin;
                    return true;
                }

            case LiteralExpressionSyntax literal:
                {
                    return TryTranslateNumericLiteral(literal, out translated, out reason);
                }

            case IdentifierNameSyntax identifier:
                {
                    if (invocationParameter is not null &&
                        identifier.Identifier.Text == invocationParameter.Name)
                    {
                        translated = identifier.Identifier.Text;
                        usesBuiltin = true;
                        return true;
                    }

                    if (semanticModel.GetSymbolInfo(identifier).Symbol is IParameterSymbol resourceParameter &&
                        resourceBindings.ContainsKey(resourceParameter))
                    {
                        translated = identifier.Identifier.Text;
                        return true;
                    }

                    reason = $"Unsupported identifier '{identifier.Identifier.Text}' in executable body.";
                    return false;
                }

            case ParenthesizedExpressionSyntax parenthesized:
                {
                    if (TryTranslateValueExpression(parenthesized.Expression, semanticModel, invocationParameter, resourceBindings,
                            out var inner, out var innerUsesBuiltin, out reason, out diagnosticId))
                    {
                        translated = $"({inner})";
                        usesBuiltin = innerUsesBuiltin;
                        return true;
                    }

                    return false;
                }

            case MemberAccessExpressionSyntax memberAccess:
                {
                    if (TryGetSymbol(memberAccess, semanticModel, out var memberSymbol) &&
                        memberSymbol is IPropertySymbol property &&
                        property.Name == "Length" &&
                        property.Parameters.Length == 0 &&
                        property.Type.SpecialType == SpecialType.System_UInt32 &&
                        property.ContainingType.MetadataName == "ShaderStorageBuffer" &&
                        property.ContainingType.ContainingNamespace.ToDisplayString() == "Delta.Shader" &&
                        TryTranslateResourceTarget(memberAccess.Expression, semanticModel, resourceBindings, out var resourceName, out reason))
                    {
                        translated = $"{resourceName}.data.length()";
                        return true;
                    }

                    reason = "Only StorageBuffer.Length is supported as a member value in executable MVP body.";
                    return false;
                }

            case PrefixUnaryExpressionSyntax unary when unary.OperatorToken.ValueText == "-":
                {
                    if (TryTranslateValueExpression(unary.Operand, semanticModel, invocationParameter, resourceBindings,
                            out var inner, out var innerUsesBuiltin, out reason, out diagnosticId))
                    {
                        translated = $"-{inner}";
                        usesBuiltin = innerUsesBuiltin;
                        return true;
                    }

                    return false;
                }

            case BinaryExpressionSyntax binary:
                {
                    if (!TryTranslateBinaryOperator(binary.OperatorToken.ValueText, out var op))
                    {
                        reason = $"Unsupported binary operator '{binary.OperatorToken}' in executable body.";
                        return false;
                    }

                    if (!TryTranslateValueExpression(binary.Left, semanticModel, invocationParameter, resourceBindings,
                            out var left, out var leftBuiltin, out reason, out diagnosticId))
                    {
                        return false;
                    }

                    if (!TryTranslateValueExpression(binary.Right, semanticModel, invocationParameter, resourceBindings,
                            out var right, out var rightBuiltin, out reason, out diagnosticId))
                    {
                        return false;
                    }

                    translated = $"{left} {op} {right}";
                    usesBuiltin = leftBuiltin || rightBuiltin;
                    return true;
                }

            case ConditionalExpressionSyntax conditional:
                {
                    if (!TryTranslateConditionExpression(conditional.Condition, semanticModel, invocationParameter, resourceBindings,
                            out var condition, out var conditionUsesBuiltin, out reason, out diagnosticId))
                    {
                        return false;
                    }

                    if (!TryTranslateValueExpression(conditional.WhenTrue, semanticModel, invocationParameter, resourceBindings,
                            out var whenTrue, out var trueUsesBuiltin, out reason, out diagnosticId))
                    {
                        return false;
                    }

                    if (!TryTranslateValueExpression(conditional.WhenFalse, semanticModel, invocationParameter, resourceBindings,
                            out var whenFalse, out var falseUsesBuiltin, out reason, out diagnosticId))
                    {
                        return false;
                    }

                    translated = $"({condition} ? {whenTrue} : {whenFalse})";
                    usesBuiltin = conditionUsesBuiltin || trueUsesBuiltin || falseUsesBuiltin;
                    return true;
                }

            case InvocationExpressionSyntax invocation:
                {
                    if (TryGetSymbol(invocation, semanticModel, out var symbol) &&
                        symbol is not null &&
                        symbol.Name == "Load" &&
                        symbol.Parameters.Length == 1 &&
                        invocation.ArgumentList.Arguments.Count == 1 &&
                        invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        if (!TryTranslateIndexExpression(invocation.ArgumentList.Arguments[0].Expression, invocationParameter, semanticModel,
                                out var index, out var indexUsesBuiltin, out reason))
                        {
                            return false;
                        }

                        if (!TryTranslateResourceTarget(memberAccess.Expression, semanticModel, resourceBindings, out var resourceName, out reason))
                        {
                            return false;
                        }

                        translated = $"{resourceName}.data[{index}]";
                        usesBuiltin = indexUsesBuiltin;
                        return true;
                    }

                    if (symbol is not null &&
                        _intrinsics.TryGetIntrinsic(symbol, out var intrinsic))
                    {
                        if (!intrinsic.SupportsStage(ShaderStage.Compute))
                        {
                            reason = $"Intrinsic '{symbol.Name}' is not valid in compute stage.";
                            return false;
                        }

                        var arguments = new List<string>(invocation.ArgumentList.Arguments.Count);
                        var intrinsicUsesBuiltin = false;
                        foreach (var argument in invocation.ArgumentList.Arguments)
                        {
                            if (!TryTranslateValueExpression(argument.Expression, semanticModel, invocationParameter, resourceBindings,
                                    out var translatedArgument, out var argumentUsesBuiltin, out reason, out diagnosticId))
                            {
                                return false;
                            }

                            arguments.Add(translatedArgument);
                            intrinsicUsesBuiltin |= argumentUsesBuiltin;
                        }

                        translated = $"{intrinsic.GlslName}({string.Join(", ", arguments)})";
                        usesBuiltin = intrinsicUsesBuiltin;
                        return true;
                    }

                    reason = "Unsupported method call in executable body.";
                    return false;
                }

            case ElementAccessExpressionSyntax elementAccess:
                {
                    if (elementAccess.ArgumentList.Arguments.Count != 1 ||
                        !TryTranslateIndexExpression(elementAccess.ArgumentList.Arguments[0].Expression, invocationParameter, semanticModel,
                            out var index, out var indexUsesBuiltin, out reason))
                    {
                        reason ??= "Indexed storage-buffer access requires exactly one index.";
                        return false;
                    }

                    if (!TryTranslateResourceTarget(elementAccess.Expression, semanticModel, resourceBindings, out var bufferName, out reason))
                    {
                        return false;
                    }

                    translated = $"{bufferName}.data[{index}]";
                    usesBuiltin = indexUsesBuiltin;
                    return true;
                }

            default:
                reason = "Unsupported expression in executable body.";
                return false;
        }
    }

    private bool TryTranslateNumericLiteral(
        LiteralExpressionSyntax literal,
        out string translated,
        out string? reason)
    {
        reason = null;
        if (literal.Token.Value is null)
        {
            reason = "Unsupported numeric literal in executable body.";
            translated = string.Empty;
            return false;
        }

        return literal.Token.Value switch
        {
            int intValue => TryTranslateConstantValue(intValue, out translated),
            uint uintValue => TryTranslateConstantValue(uintValue, out translated),
            long longValue => TryTranslateConstantValue((long)longValue, out translated),
            ulong ulongValue => TryTranslateConstantValue((ulong)ulongValue, out translated),
            float floatValue => TryTranslateConstantValue(floatValue, out translated),
            double doubleValue => TryTranslateConstantValue(doubleValue, out translated),
            _ => ThrowLiteralFallback(out translated, out reason)
        };
    }

    private bool ThrowLiteralFallback(out string translated, out string? reason)
    {
        reason = "Unsupported numeric literal type in executable body.";
        translated = string.Empty;
        return false;
    }

    private bool TryTranslateResourceTarget(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out string target,
        out string? reason)
    {
        reason = null;
        target = string.Empty;

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not IParameterSymbol parameter)
        {
            reason = "Only direct parameter access is supported for storage buffer operations.";
            return false;
        }

        if (!resourceBindings.ContainsKey(parameter))
        {
            reason = "Storage buffer operation target is not a shader storage buffer parameter.";
            return false;
        }

        target = parameter.Name;
        return true;
    }

    private bool TryTranslateBinaryOperator(string token, out string mapped)
    {
        mapped = token switch
        {
            "+" => "+",
            "-" => "-",
            "*" => "*",
            "/" => "/",
            "%" => "%",
            "<" => "<",
            ">" => ">",
            "<=" => "<=",
            ">=" => ">=",
            "==" => "==",
            "!=" => "!=",
            "&&" => "&&",
            "||" => "||",
            _ => string.Empty
        };

        return mapped.Length != 0;
    }

    private bool TryTranslateConditionExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<IParameterSymbol, uint> resourceBindings,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return TryTranslateConditionExpression(parenthesized.Expression, semanticModel, invocationParameter, resourceBindings,
                out translated, out usesBuiltin, out reason, out diagnosticId);
        }

        if (expression is not BinaryExpressionSyntax binary ||
            !TryTranslateBinaryOperator(binary.OperatorToken.ValueText, out var op))
        {
            reason = "Conditional expressions require a supported comparison or logical binary operator.";
            return false;
        }

        if (!TryTranslateValueExpression(binary.Left, semanticModel, invocationParameter, resourceBindings,
                out var left, out var leftUsesBuiltin, out reason, out diagnosticId) ||
            !TryTranslateValueExpression(binary.Right, semanticModel, invocationParameter, resourceBindings,
                out var right, out var rightUsesBuiltin, out reason, out diagnosticId))
        {
            return false;
        }

        translated = $"{left} {op} {right}";
        usesBuiltin = leftUsesBuiltin || rightUsesBuiltin;
        return true;
    }

    private bool TryTranslateConstantValue(int value, out string translated)
    {
        translated = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryTranslateConstantValue(uint value, out string translated)
    {
        translated = value.ToString(CultureInfo.InvariantCulture) + "u";
        return true;
    }

    private bool TryTranslateConstantValue(long value, out string translated)
    {
        translated = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryTranslateConstantValue(ulong value, out string translated)
    {
        translated = value.ToString(CultureInfo.InvariantCulture) + "u";
        return true;
    }

    private bool TryTranslateConstantValue(float value, out string translated)
    {
        translated = value.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryTranslateConstantValue(double value, out string translated)
    {
        translated = value.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryGetSymbol(InvocationExpressionSyntax invocation, SemanticModel semanticModel, out IMethodSymbol? symbol)
    {
        symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return symbol is not null;
    }

    private bool TryGetSymbol(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel, out ISymbol? symbol)
    {
        symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
        return symbol is not null;
    }
}
