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
    private readonly Dictionary<ILocalSymbol, string> _locals = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _localNames = new(StringComparer.Ordinal);
    private IParameterSymbol? _contextParameter;

    public ComputeShaderBodyTranslator(IntrinsicRegistry intrinsics)
    {
        _intrinsics = intrinsics;
    }

    public bool TryTranslate(
        IMethodSymbol method,
        MethodDeclarationSyntax methodSyntax,
        SemanticModel semanticModel,
        IParameterSymbol? contextParameter,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        out ComputeShaderBodyTranslationResult? result,
        out string? reason,
        out string diagnosticId)
    {
        reason = null;
        result = null;
        diagnosticId = ShaderDiagnosticId.DSH008;
        _contextParameter = contextParameter;
        _locals.Clear();
        _localNames.Clear();

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

        if (!TryTranslateBlock(body, semanticModel, invocationParameter, resourceBindings,
                out var translatedBody, out var usesBuiltin, out reason, out diagnosticId))
        {
            return false;
        }

        result = new ComputeShaderBodyTranslationResult
        {
            UsesBuiltinInvocationId = usesBuiltin,
            Body = translatedBody
        };

        return true;
    }

    private bool TryTranslateBlock(
        BlockSyntax block,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        var statements = new List<string>(block.Statements.Count);
        usesBuiltin = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        foreach (var statement in block.Statements)
        {
            if (!TryTranslateStatement(statement, semanticModel, invocationParameter, resourceBindings,
                    out var translatedStatement, out var statementUsesBuiltin, out reason, out diagnosticId))
            {
                translated = string.Empty;
                return false;
            }

            if (translatedStatement.Length != 0)
            {
                statements.Add(translatedStatement);
            }

            usesBuiltin |= statementUsesBuiltin;
        }

        translated = string.Join("\n", statements);
        return true;
    }

    private bool TryTranslateStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
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
            return TryTranslateBlock(block, semanticModel, invocationParameter, resourceBindings,
                out translated, out usesBuiltin, out reason, out diagnosticId);
        }

        if (statement is LocalDeclarationStatementSyntax declaration)
        {
            if (declaration.Declaration.Variables.Count != 1 || declaration.Declaration.Variables[0].Initializer is not { } initializer)
            {
                reason = "Local declarations require exactly one initialized variable in a compute shader body.";
                return false;
            }

            var variable = declaration.Declaration.Variables[0];
            var local = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
            if (local is null || !TryMapLocalType(local.Type, out var localType))
            {
                reason = "Local declaration has an unsupported shader type.";
                return false;
            }

            if (!TryTranslateValueExpression(initializer.Value, semanticModel, invocationParameter, resourceBindings,
                    out var initializerText, out usesBuiltin, out reason, out diagnosticId))
            {
                return false;
            }

            var localName = CreateLocalName(local.Name);
            _locals[local] = localName;
            translated = $"{localType} {localName} = {initializerText};";
            return true;
        }

        if (statement is IfStatementSyntax ifStatement)
        {
            if (!TryTranslateConditionExpression(ifStatement.Condition, semanticModel, invocationParameter, resourceBindings,
                    out var condition, out var conditionUsesBuiltin, out reason, out diagnosticId))
            {
                return false;
            }

            if (!TryTranslateStatement(ifStatement.Statement, semanticModel, invocationParameter, resourceBindings,
                    out var whenTrue, out var trueUsesBuiltin, out reason, out diagnosticId))
            {
                return false;
            }

            translated = $"if ({condition})\n{{\n{Indent(whenTrue)}\n}}";
            usesBuiltin = conditionUsesBuiltin || trueUsesBuiltin;

            if (ifStatement.Else is { } elseClause)
            {
                if (!TryTranslateStatement(elseClause.Statement, semanticModel, invocationParameter, resourceBindings,
                        out var whenFalse, out var falseUsesBuiltin, out reason, out diagnosticId))
                {
                    return false;
                }

                translated += $"\nelse\n{{\n{Indent(whenFalse)}\n}}";
                usesBuiltin |= falseUsesBuiltin;
            }

            return true;
        }

        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            return TryTranslateExpressionStatement(expressionStatement, semanticModel, invocationParameter, resourceBindings,
                out translated, out usesBuiltin, out reason, out diagnosticId);
        }

        reason = "Unsupported compute statement in executable shader body.";
        return false;
    }

    private bool TryTranslateExpressionStatement(
        ExpressionStatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out string diagnosticId)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH008;

        if (statement.Expression is AssignmentExpressionSyntax assignment &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            assignment.Left is IdentifierNameSyntax localIdentifier &&
            semanticModel.GetSymbolInfo(localIdentifier).Symbol is ILocalSymbol local &&
            _locals.TryGetValue(local, out var localName))
        {
            if (!TryTranslateValueExpression(assignment.Right, semanticModel, invocationParameter, resourceBindings,
                    out var value, out usesBuiltin, out reason, out diagnosticId))
            {
                return false;
            }

            translated = $"{localName} = {value};";
            return true;
        }

        return TryTranslateStoreExpression(statement, semanticModel, invocationParameter, resourceBindings,
            out translated, out usesBuiltin, out reason, out diagnosticId);
    }

    private bool TryMapLocalType(ITypeSymbol type, out string glslType)
    {
        if (_intrinsics.TryMapType(type, out glslType))
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

        return glslType.Length != 0;
    }

    private string CreateLocalName(string name)
    {
        var baseName = SanitizeName(name);
        var candidate = baseName;
        var suffix = 2;
        while (!_localNames.Add(candidate))
        {
            candidate = baseName + "_" + suffix++;
        }

        return candidate;
    }

    private static string Indent(string value)
        => string.Join("\n", value.Split('\n').Select(line => "    " + line));

    private bool TryTranslateStoreExpression(
        StatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
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
                reason = "Indexed assignment block must contain a single expression statement.";
                return false;
            }

            statement = block.Statements[0];
        }

        if (statement is not ExpressionStatementSyntax exprStmt)
        {
            reason = "Indexed assignment must be a single expression statement.";
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

        reason = "Only indexed storage-buffer assignment is supported in executable compute bodies.";
        return false;
    }

    private bool TryTranslateInvocationCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? invocationParameter,
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
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
            new Dictionary<ISymbol, uint>(SymbolEqualityComparer.Default),
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
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
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
                    if (semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
                        _locals.TryGetValue(local, out var localName))
                    {
                        translated = localName;
                        return true;
                    }

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
                    if (TryTranslateContextMember(memberAccess, semanticModel, out translated, out usesBuiltin))
                    {
                        return true;
                    }

                    if (TryTranslateShaderBuiltinMember(memberAccess, semanticModel, out translated,
                            out usesBuiltin, out reason, out var builtinRecognized))
                    {
                        return true;
                    }

                    if (builtinRecognized)
                    {
                        return false;
                    }

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
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
        out string target,
        out string? reason)
    {
        reason = null;
        target = string.Empty;

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is IParameterSymbol parameter && resourceBindings.ContainsKey(parameter))
        {
            target = parameter.Name;
            return true;
        }

        if (symbol is IFieldSymbol field && IsContextStorageField(field))
        {
            target = field.Name;
            return true;
        }

        reason = "Storage buffer operation target is not a declared shader storage buffer.";
        return false;
    }

    private bool TryTranslateContextMember(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        out string translated,
        out bool usesBuiltin)
    {
        translated = string.Empty;
        usesBuiltin = false;

        if (_contextParameter is null)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(memberAccess).Symbol is not IFieldSymbol field ||
            !IsContextField(field))
        {
            return false;
        }

        if (HasContextAttribute(field, "Delta.Shader.PushConstantAttribute"))
        {
            translated = "pushConstants.member_" + SanitizeName(field.Name);
            return true;
        }

        if (IsContextResourceField(field))
        {
            translated = field.Name;
            return true;
        }

        return false;
    }

    private bool TryTranslateShaderBuiltinMember(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        out string translated,
        out bool usesBuiltin,
        out string? reason,
        out bool recognized)
    {
        translated = string.Empty;
        usesBuiltin = false;
        reason = null;
        recognized = false;

        if (semanticModel.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property &&
            _intrinsics.TryGetIntrinsic(property, out var directBinding) &&
            directBinding.Category == IntrinsicCategory.Builtin)
        {
            recognized = true;
            if (!directBinding.SupportsStage(ShaderStage.Compute))
            {
                reason = $"Shader builtin '{property.Name}' is not valid in compute stage.";
                return false;
            }

            translated = directBinding.GlslName;
            usesBuiltin = true;
            return true;
        }

        if (memberAccess.Expression is not MemberAccessExpressionSyntax parent ||
            semanticModel.GetSymbolInfo(parent).Symbol is not IPropertySymbol parentProperty ||
            !_intrinsics.TryGetIntrinsic(parentProperty, out var parentBinding) ||
            parentBinding.Category != IntrinsicCategory.Builtin)
        {
            return false;
        }

        recognized = true;
        if (!parentBinding.SupportsStage(ShaderStage.Compute))
        {
            reason = $"Shader builtin '{parentProperty.Name}' is not valid in compute stage.";
            return false;
        }

        var component = GetBuiltinComponent(memberAccess.Name.Identifier.ValueText);
        if (component.Length == 0)
        {
            reason = $"Unsupported component '{memberAccess.Name.Identifier.ValueText}' on shader builtin '{parentProperty.Name}'.";
            return false;
        }

        translated = parentBinding.GlslName + "." + component;
        usesBuiltin = true;
        return true;
    }

    private bool IsContextField(IFieldSymbol field)
        => _contextParameter is not null &&
           SymbolEqualityComparer.Default.Equals(field.ContainingType, _contextParameter.Type);

    private bool IsContextStorageField(IFieldSymbol field)
        => IsContextField(field) && HasContextAttribute(field, "Delta.Shader.LayoutAttribute");

    private bool IsContextResourceField(IFieldSymbol field)
        => IsContextField(field) && HasContextAttribute(field, "Delta.Shader.LayoutAttribute");

    private static bool HasContextAttribute(IFieldSymbol field, string metadataName)
        => field.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static string SanitizeName(string name)
        => string.Concat(name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_'));

    private static string GetBuiltinComponent(string name)
        => name switch
        {
            "X" or "x" => "x",
            "Y" or "y" => "y",
            "Z" or "z" => "z",
            "W" or "w" => "w",
            _ => string.Empty
        };

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
        IReadOnlyDictionary<ISymbol, uint> resourceBindings,
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
