using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader;
using Delta.Shader.Compiler.Intrinsics;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

public static class ComputeEntryPoints
{
    public static ShaderCompilationResult ValidateAndBuild(
        ModuleCompilationContext context,
        RoslynFrontend frontend,
        ShaderCompilationOptions? options = null,
        string? entryPointName = null,
        string? entryPointIdentity = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<ShaderDiagnostic>();
        var entries = frontend.FindComputeEntryPoints()
            .Where(entry => (entryPointName is null || entry.Method.Name == entryPointName) &&
                (entryPointIdentity is null || ShaderMethodIdentity.Get(entry.Method) == entryPointIdentity))
            .ToArray();

        if (entries.Length == 0)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "No valid [ComputeShader] entry point found.",
                Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(string.Empty, false, diagnostics);
        }

        if (entries.Length > 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "MVP supports one [ComputeShader] entry point per module.",
                Severity: ShaderDiagnosticSeverity.Error));
        }

        if (!ValidateProfileCompatibility(resultOptions, out var profileError))
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH007,
                profileError ?? "The selected shader profile is not compatible with the compiler.",
                Severity: ShaderDiagnosticSeverity.Error));
        }

        var entry = entries[0];
        var resources = new List<ShaderIrResource>();
        var pushConstants = new List<ShaderIrPushConstant>();
        var seenBindings = new HashSet<(uint Set, uint Binding)>();
        var storageBuffers = new Dictionary<ISymbol, uint>(SymbolEqualityComparer.Default);
        var structDefinitions = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);

        if (!entry.Method.IsStatic || !entry.Method.ReturnsVoid)
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH004,
                "[ComputeShader] entry point must be static void.",
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        if (!TryValidateLocalSize(entry, resultOptions, out var localSizeError))
        {
            var loc = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH007,
                localSizeError ?? "The compute local size is invalid.",
                loc?.Path,
                loc is null ? 0 : loc.Value.StartLinePosition.Line + 1,
                loc is null ? 0 : loc.Value.StartLinePosition.Character + 1));
        }

        var contextParameter = entry.Method.Parameters.Length == 1 &&
            ShaderVisibleTypeValidation.IsContextParameter(entry.Method.Parameters[0], context.Compilation)
            ? entry.Method.Parameters[0]
            : null;

        if (contextParameter is null)
        {
            diagnostics.Add(CreateDiagnostic(entry.Method, ShaderDiagnosticId.DSH002,
                "[ComputeShader] entry point must have exactly one 'in' shader context parameter."));
        }
        else
        {
            foreach (var issue in ShaderVisibleTypeValidation.ValidateContext(contextParameter, context.Compilation))
            {
                diagnostics.Add(CreateDiagnostic(issue.Symbol, issue.Id, issue.Message));
            }

            if (diagnostics.Count == 0 &&
                !TryBuildContextContract(contextParameter, context, seenBindings, storageBuffers, structDefinitions,
                    resources, pushConstants, out var contextDiagnostic))
            {
                if (contextDiagnostic is not null)
                {
                    diagnostics.Add(contextDiagnostic);
                }
            }
        }

        string body = string.Empty;
        IReadOnlyList<string> helperFunctions = [];
        IReadOnlyDictionary<IMethodSymbol, string> helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        IReadOnlyDictionary<IMethodSymbol, bool> helperReceivers = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool usesBuiltinInvocationId = false;
        if (diagnostics.Count == 0)
        {
            var methodSyntax = entry.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            string? helperDiagnosticReason = null;
            if (methodSyntax is null)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH008,
                    "Unable to read compute shader entry-point source body.",
                    entry.Method.Locations.FirstOrDefault()?.GetLineSpan().Path));
            }
            else if (!TryBuildLocalStructs(methodSyntax, context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree), context,
                    structDefinitions, out var structDiagnosticReason))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH008,
                    structDiagnosticReason ?? "Unable to build local compute shader structs.",
                    entry.Method.Locations.FirstOrDefault()?.GetLineSpan().Path));
            }
            else if (!TryBuildHelpers(methodSyntax, context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree), context,
                    structDefinitions,
                    out helperFunctions, out helperNames, out helperReceivers, out helperDiagnosticReason))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH008,
                    helperDiagnosticReason ?? "Unable to lower compute shader helper call graph.",
                    entry.Method.Locations.FirstOrDefault()?.GetLineSpan().Path));
            }
            else
            {
                CreateStructMaps(structDefinitions, out var structNames, out var structFields, out var structProperties);
                if (!ShaderBodyTranslator.TryTranslateCompute(
                        methodSyntax,
                        context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree),
                        context,
                        contextParameter,
                        storageBuffers,
                        helperNames,
                        structNames,
                        structFields,
                        structProperties,
                        out body,
                        out usesBuiltinInvocationId,
                        out var bodyDiagnosticReason,
                        helperReceivers: helperReceivers))
                {
                    var location = entry.Method.Locations.FirstOrDefault()?.GetLineSpan();
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH008,
                        bodyDiagnosticReason ?? "Compute entry point body is not supported in MVP.",
                        location?.Path,
                        location is null ? 0 : location.Value.StartLinePosition.Line + 1,
                        location is null ? 0 : location.Value.StartLinePosition.Character + 1));
                }
            }
        }

        var module = new ShaderIrModule
        {
            Stage = ShaderStage.Compute,
            SourceEntryPointName = entry.Name,
            EntryPointName = entry.Name,
            LocalSizeX = entry.LocalSizeX,
            LocalSizeY = entry.LocalSizeY,
            LocalSizeZ = entry.LocalSizeZ,
            Resources = resources,
            Structs = structDefinitions.Values.OrderBy(structure => structure.GlslName, StringComparer.Ordinal).ToArray(),
            Requirements = [$"Vulkan {resultOptions.Profile}", $"GLSL {resultOptions.Glsl}", $"SPIRV {resultOptions.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
            Body = body,
            HelperFunctions = context.Intrinsics.GetGlslHelperFunctions(
                    ShaderStage.Compute,
                    new[] { body }.Concat(helperFunctions))
                .Concat(helperFunctions)
                .ToArray(),
            UsesBuiltinInvocationId = usesBuiltinInvocationId,
            InvocationParameterName = null,
            PushConstants = pushConstants
        };

        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, resultOptions, entry.Method.Name, ShaderMethodIdentity.Get(entry.Method));
    }

    private static bool TryBuildHelpers(
        MethodDeclarationSyntax entrySyntax,
        SemanticModel entryModel,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out IReadOnlyList<string> functions,
        out IReadOnlyDictionary<IMethodSymbol, string> names,
        out IReadOnlyDictionary<IMethodSymbol, bool> receivers,
        out string? reason)
    {
        CreateStructMaps(structDefinitions, out var structNames, out var structFields, out var structProperties);
        var ordered = new List<IMethodSymbol>();
        var states = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        var helperReceivers = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        receivers = helperReceivers;
        reason = null;
        string? failureReason = null;

        bool EnsureStructType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Struct ||
                TryMapComputeType(type, context, structNames, out _) ||
                ShaderStructSupport.IsStateless(namedType) ||
                structDefinitions.ContainsKey(namedType))
            {
                return true;
            }

            return TryBuildStructLayout(
                namedType,
                context,
                structDefinitions,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
                out _,
                out failureReason);
        }

        bool Visit(IMethodSymbol method)
        {
            var definition = method.OriginalDefinition;
            if (states.TryGetValue(method, out var state))
            {
                if (state == 1)
                {
                    failureReason = $"Recursive compute shader helper call graph at '{definition.Name}'.";
                    return false;
                }

                return true;
            }

            if (!definition.IsStatic && definition.ContainingType.TypeKind != TypeKind.Struct)
            {
                failureReason = $"Compute shader helper '{definition.Name}' must be static or an instance method on a value struct.";
                return false;
            }
            if ((method.IsGenericMethod && method.TypeArguments.Any(argument => argument is ITypeParameterSymbol)) ||
                method.ReturnsVoid && !definition.Parameters.Any(parameter => parameter.RefKind == RefKind.Out) ||
                definition.Parameters.Any(parameter => parameter.RefKind != RefKind.None && parameter.RefKind != RefKind.Out))
            {
                failureReason = $"Compute shader helper '{definition.Name}' must be a non-generic value method or a void method with out parameters.";
                return false;
            }

            if (definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax syntax)
            {
                failureReason = $"Compute shader helper '{definition.Name}' must be declared in the shader source project.";
                return false;
            }

            SyntaxNode? helperBody = syntax.ExpressionBody is { Expression: { } expressionBody }
                ? expressionBody
                : syntax.Body;
            if (helperBody is null)
            {
                failureReason = $"Compute shader helper '{definition.Name}' must have a translatable body.";
                return false;
            }

            var hasReceiver = !definition.IsStatic && !ShaderStructSupport.IsStateless(method.ContainingType);
            if (hasReceiver && !EnsureStructType(method.ContainingType))
            {
                return false;
            }
            if (!EnsureStructType(method.ReturnType))
            {
                return false;
            }
            foreach (var parameter in method.Parameters)
            {
                if (!EnsureStructType(parameter.Type))
                {
                    return false;
                }
            }

            CreateStructMaps(structDefinitions, out structNames, out structFields, out structProperties);
            if (!TryMapComputeType(method.ReturnType, context, structNames, out _) ||
                (hasReceiver && !TryMapComputeType(method.ContainingType, context, structNames, out _)) ||
                method.Parameters.Any(parameter => !TryMapComputeType(parameter.Type, context, structNames, out _)))
            {
                failureReason = $"Compute shader helper '{definition.Name}' has an unsupported parameter or return type.";
                return false;
            }

            if (definition.IsStatic && helperBody.DescendantNodesAndSelf().OfType<ThisExpressionSyntax>().Any())
            {
                failureReason = $"Static compute shader helper '{definition.Name}' cannot use an instance receiver.";
                return false;
            }

            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            if (!ShaderBodyTranslator.ValidateOutParameters(syntax, model, method, out failureReason))
            {
                return false;
            }

            foreach (var assignment in helperBody.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                if (model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol)
                {
                    failureReason = $"Compute shader helper '{definition.Name}' cannot mutate a property.";
                    return false;
                }
            }

            foreach (var identifier in helperBody.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol is not null &&
                    context.Intrinsics.TryGetIntrinsic(symbol, out var memberBinding) &&
                    memberBinding.Category is IntrinsicCategory.Builtin or IntrinsicCategory.Swizzle)
                {
                    continue;
                }

                if (symbol is IFieldSymbol field && !field.HasConstantValue)
                {
                    if (!definition.IsStatic && !field.IsStatic &&
                        SymbolEqualityComparer.Default.Equals(field.ContainingType, definition.ContainingType) &&
                        (structFields.ContainsKey(field) || IsCompileTimeOnlyMember(field, method)))
                    {
                        continue;
                    }

                    failureReason = $"Compute shader helper '{definition.Name}' captures managed field '{field.Name}'.";
                    return false;
                }

                if (symbol is IPropertySymbol property &&
                    !context.Intrinsics.TryGetIntrinsic(symbol, out _) &&
                    !IsSupportedShaderProperty(property, definition, structProperties))
                {
                    failureReason = $"Compute shader helper '{definition.Name}' uses unsupported property state.";
                    return false;
                }
            }

            states[method] = 1;
            helperNames[method] = CreateHelperName(method, usedNames);
            helperReceivers[method] = hasReceiver;
            foreach (var invocation in helperBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
                {
                    failureReason = $"Compute shader helper '{definition.Name}' contains an unresolved method call.";
                    return false;
                }

                if (context.Intrinsics.TryGetIntrinsic(called, out var intrinsic))
                {
                    if (!intrinsic.SupportsStage(ShaderStage.Compute))
                    {
                        failureReason = $"Intrinsic '{called.Name}' is not valid in compute stage.";
                        return false;
                    }

                    continue;
                }

                var target = ShaderHelperSpecialization.ResolveTarget(invocation, called, model, method);
                if (!Visit(target))
                {
                    return false;
                }

                if (!SymbolEqualityComparer.Default.Equals(target, called))
                {
                    helperNames[called] = helperNames[target];
                    helperReceivers[called] = helperReceivers[target];
                }
            }

            states[method] = 2;
            ordered.Add(method);
            return true;
        }

        var entryBody = entrySyntax.Body ?? (SyntaxNode?)entrySyntax.ExpressionBody?.Expression;
        if (entryBody is not null)
        {
            var entryMethod = entryModel.GetDeclaredSymbol(entrySyntax) as IMethodSymbol;
            foreach (var invocation in entryBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (entryModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
                {
                    continue;
                }

                if (context.Intrinsics.TryGetIntrinsic(called, out var intrinsic))
                {
                    if (!intrinsic.SupportsStage(ShaderStage.Compute))
                    {
                        reason = $"Intrinsic '{called.Name}' is not valid in compute stage.";
                        functions = [];
                        names = helperNames;
                        return false;
                    }

                    continue;
                }

                var target = ShaderHelperSpecialization.ResolveTarget(invocation, called, entryModel, entryMethod ?? called);
                if (!Visit(target))
                {
                    reason = failureReason;
                    functions = [];
                    names = helperNames;
                    return false;
                }

                if (!SymbolEqualityComparer.Default.Equals(target, called))
                {
                    helperNames[called] = helperNames[target];
                    helperReceivers[called] = helperReceivers[target];
                }
            }
        }

        CreateStructMaps(structDefinitions, out structNames, out structFields, out structProperties);
        var emitted = new List<string>(ordered.Count);
        foreach (var helper in ordered)
        {
            if (helper.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax syntax ||
                syntax.ExpressionBody?.Expression is null && syntax.Body is null)
            {
                reason = $"Compute shader helper '{helper.Name}' has no translatable body.";
                functions = [];
                names = helperNames;
                return false;
            }

            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            var signature = new List<string>(helper.Parameters.Length + (helper.IsStatic ? 0 : 1));
            string? instanceReceiver = null;
            var hasReceiver = helperReceivers.TryGetValue(helper, out var receiver) ? receiver : !helper.IsStatic;
            if (hasReceiver)
            {
                if (!TryMapComputeType(helper.ContainingType, context, structNames, out var receiverType))
                {
                    reason = $"Compute shader helper '{helper.Name}' has an unsupported value-struct receiver type.";
                    functions = [];
                    names = helperNames;
                    return false;
                }

                instanceReceiver = "self";
                signature.Add(receiverType + " " + instanceReceiver);
            }

            foreach (var parameter in helper.Parameters)
            {
                if (!TryMapComputeType(parameter.Type, context, structNames, out var glslType))
                {
                    reason = $"Compute shader helper '{helper.Name}' has an unsupported parameter type.";
                    functions = [];
                    names = helperNames;
                    return false;
                }

                var parameterName = "arg_" + SanitizeHelperName(parameter.Name);
                parameterMap[parameter] = parameterName;
                if (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition))
                {
                    parameterMap[parameter.OriginalDefinition] = parameterName;
                }
                signature.Add((parameter.RefKind == RefKind.Out ? "out " : string.Empty) + glslType + " " + parameterName);
            }

            if (!TryMapComputeType(helper.ReturnType, context, structNames, out var returnType))
            {
                reason = $"Compute shader helper '{helper.Name}' has an unsupported return type.";
                functions = [];
                names = helperNames;
                return false;
            }

            var outputParameters = helper.Parameters
                .Where(parameter => parameter.RefKind == RefKind.Out)
                .ToArray();
            bool translationSucceeded;
            string translation;
            string? helperReason;
            if (syntax.ExpressionBody?.Expression is { } expressionBody)
            {
                translationSucceeded = ShaderBodyTranslator.TryTranslateComputeExpression(
                    expressionBody,
                    model,
                    context,
                    null,
                    new Dictionary<ISymbol, uint>(SymbolEqualityComparer.Default),
                    new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default),
                    parameterMap,
                    helperNames,
                    out translation,
                    out _,
                    out helperReason,
                    instanceReceiver: instanceReceiver,
                    structNames: structNames,
                    structFields: structFields,
                    structProperties: structProperties,
                    helperReceivers: helperReceivers);
            }
            else
            {
                translationSucceeded = ShaderBodyTranslator.TryTranslateCompute(
                    syntax,
                    model,
                    context,
                    null,
                    new Dictionary<ISymbol, uint>(SymbolEqualityComparer.Default),
                    helperNames,
                    structNames,
                    structFields,
                    structProperties,
                    out translation,
                    out _,
                    out helperReason,
                    parameterMap,
                    outputParameters,
                    allowValueReturn: true,
                    instanceReceiver: instanceReceiver,
                    helperReceivers: helperReceivers);
            }

            if (!translationSucceeded)
            {
                reason = helperReason ?? $"Unable to translate compute shader helper '{helper.Name}'.";
                functions = [];
                names = helperNames;
                return false;
            }

            emitted.Add(syntax.ExpressionBody is not null
                ? helper.ReturnsVoid
                    ? $"{returnType} {helperNames[helper]}({string.Join(", ", signature)}) {{ {translation}; }}"
                    : $"{returnType} {helperNames[helper]}({string.Join(", ", signature)}) {{ return {translation}; }}"
                : $"{returnType} {helperNames[helper]}({string.Join(", ", signature)}) {{\n{translation}\n}}");
        }

        functions = emitted;
        names = helperNames;
        return true;
    }

    private static bool TryBuildLocalStructs(
        MethodDeclarationSyntax methodSyntax,
        SemanticModel model,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out string? reason)
    {
        reason = null;
        foreach (var creation in methodSyntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (model.GetTypeInfo(creation).Type is INamedTypeSymbol type &&
                type.TypeKind == TypeKind.Struct &&
                !TryMapComputeType(type, context, null, out _) &&
                !TryBuildStructLayout(type, context, structDefinitions,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out _, out reason))
            {
                return false;
            }
        }

        foreach (var creation in methodSyntax.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            var type = model.GetTypeInfo(creation).ConvertedType ?? model.GetTypeInfo(creation).Type;
            if (type is INamedTypeSymbol namedType &&
                namedType.TypeKind == TypeKind.Struct &&
                !TryMapComputeType(namedType, context, null, out _) &&
                !TryBuildStructLayout(namedType, context, structDefinitions,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out _, out reason))
            {
                return false;
            }
        }

        foreach (var declaration in methodSyntax.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            var type = declaration.Type.IsVar
                ? declaration.Variables.FirstOrDefault()?.Initializer is { } initializer
                    ? model.GetTypeInfo(initializer.Value).ConvertedType ?? model.GetTypeInfo(initializer.Value).Type
                    : null
                : model.GetTypeInfo(declaration.Type).Type;
            if (type is INamedTypeSymbol namedType &&
                namedType.TypeKind == TypeKind.Struct &&
                !TryMapComputeType(namedType, context, null, out _) &&
                !TryBuildStructLayout(namedType, context, structDefinitions,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out _, out reason))
            {
                return false;
            }
        }

        return true;
    }

    private static void CreateStructMaps(
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out Dictionary<INamedTypeSymbol, string> structNames,
        out Dictionary<IFieldSymbol, string> structFields,
        out Dictionary<IPropertySymbol, string> structProperties)
    {
        structNames = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        structFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        structProperties = new Dictionary<IPropertySymbol, string>(SymbolEqualityComparer.Default);
        foreach (var definition in structDefinitions)
        {
            structNames[definition.Key] = definition.Value.GlslName;
            foreach (var member in definition.Key.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared)
                {
                    var fieldMember = definition.Value.Members.FirstOrDefault(item => item.Name == field.Name);
                    if (fieldMember is null)
                    {
                        continue;
                    }

                    var fieldName = fieldMember.GlslName;
                    structFields[field] = fieldName;
                    structFields[field.OriginalDefinition] = fieldName;
                }
                else if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer &&
                         property.Parameters.Length == 0 && property.GetMethod is not null &&
                         ShaderStructSupport.IsAutoProperty(property))
                {
                    var propertyMember = definition.Value.Members.FirstOrDefault(item => item.Name == property.Name);
                    if (propertyMember is null)
                    {
                        continue;
                    }

                    var propertyName = propertyMember.GlslName;
                    structProperties[property] = propertyName;
                    structProperties[property.OriginalDefinition] = propertyName;
                }
            }
        }
    }

    private static bool IsSupportedShaderProperty(
        IPropertySymbol property,
        IMethodSymbol helper,
        IReadOnlyDictionary<IPropertySymbol, string> structProperties)
    {
        if (!property.IsStatic)
        {
            return !helper.IsStatic &&
                property.ContainingType is not null &&
                SymbolEqualityComparer.Default.Equals(property.ContainingType, helper.ContainingType.OriginalDefinition) &&
                (structProperties.ContainsKey(property) || IsExpressionBodiedProperty(property));
        }

        if (property.GetMethod is null || property.SetMethod is not null || property.Parameters.Length != 0)
        {
            return false;
        }

        return property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax syntax &&
            (syntax.ExpressionBody?.Expression is not null ||
             syntax.AccessorList?.Accessors.Any(accessor =>
                 accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody?.Expression is not null) == true ||
             syntax.Initializer?.Value is not null);
    }

    private static bool IsExpressionBodiedProperty(IPropertySymbol property)
        => property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax syntax &&
           (syntax.ExpressionBody?.Expression is not null ||
            syntax.AccessorList?.Accessors.Any(accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody?.Expression is not null) == true);

    private static bool IsCompileTimeOnlyMember(IFieldSymbol field, IMethodSymbol method)
    {
        if (field.ContainingType is null || method.ContainingType is null ||
            !SymbolEqualityComparer.Default.Equals(
                field.ContainingType.OriginalDefinition,
                method.ContainingType.OriginalDefinition))
        {
            return false;
        }

        return method.ContainingType.GetMembers(field.Name)
            .OfType<IFieldSymbol>()
            .Any(candidate => !candidate.IsStatic &&
                !candidate.IsImplicitlyDeclared &&
                candidate.Type is INamedTypeSymbol namedType &&
                ShaderStructSupport.IsStateless(namedType));
    }

    private static bool TryMapComputeType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        IReadOnlyDictionary<INamedTypeSymbol, string>? structNames,
        out string glslType)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            glslType = "void";
            return true;
        }

        if (ShaderEnumSupport.TryMap(type, out glslType))
        {
            return true;
        }

        if (context.Intrinsics.TryMapType(type, out glslType))
        {
            return true;
        }

        if (IsDeltaMathsHalf(type))
        {
            glslType = "float16_t";
            return true;
        }

        if (type is INamedTypeSymbol namedType && structNames is not null && structNames.TryGetValue(namedType, out glslType))
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
        return glslType.Length != 0;
    }

    private static bool IsDeltaMathsHalf(ITypeSymbol type)
        => type is INamedTypeSymbol namedType &&
           namedType.Name == "half" &&
           namedType.ContainingNamespace.ToDisplayString() == "Delta.Maths";

    private static string CreateHelperName(IMethodSymbol method, ISet<string> usedNames)
    {
        var baseName = "delta_helper_" + SanitizeHelperName(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = baseName + "_" + suffix++;
        }

        return candidate;
    }

    private static string SanitizeHelperName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static ShaderDiagnostic CreateDiagnostic(ISymbol symbol, string id, string message)
    {
        var location = symbol.Locations.FirstOrDefault()?.GetLineSpan();
        return new ShaderDiagnostic(
            id,
            message,
            location?.Path,
            location is null ? 0 : location.Value.StartLinePosition.Line + 1,
            location is null ? 0 : location.Value.StartLinePosition.Character + 1);
    }

    private static bool TryBuildContextContract(
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        Dictionary<ISymbol, uint> resourceBindings,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        List<ShaderIrResource> resources,
        List<ShaderIrPushConstant> pushConstants,
        out ShaderDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (contextParameter.Type is not INamedTypeSymbol contextType)
        {
            diagnostic = CreateDiagnostic(contextParameter, ShaderDiagnosticId.DSH002,
                "Shader context parameter must be a user-defined value type.");
            return false;
        }

        var pushMembers = new List<ShaderIrStructMember>();
        uint pushOffset = 0;
        uint pushAlignment = 1;
        foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            var attributes = field.GetAttributes()
                .Where(attribute => IsContextFieldAttribute(attribute.AttributeClass, context))
                .ToArray();
            if (attributes.Length == 0)
            {
                diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                    $"Shader context field '{field.Name}' must declare a resource, push constant, or builtin role.");
                return false;
            }

            if (attributes.Length > 1)
            {
                diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                    $"Shader context field '{field.Name}' has more than one shader role attribute.");
                return false;
            }

            var attribute = attributes[0];
            if (IsLayoutAttribute(attribute.AttributeClass, context))
            {
                if (attribute.ConstructorArguments.Length == 1)
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        $"Vertex-input [Layout(location)] is not valid in compute context field '{field.Name}'.");
                    return false;
                }

                if (attribute.ConstructorArguments.Length != 2)
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        $"Descriptor [Layout(set, binding)] on context field '{field.Name}' requires two constant arguments.");
                    return false;
                }

                if (SymbolEqualityComparer.Default.Equals(field.Type, context.SampledTexture2DType))
                {
                    if (!TryBuildContextTextureResource(field, contextParameter, context, seenBindings,
                            out var texture, out var textureReason))
                    {
                        diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                            textureReason ?? "Unsupported context sampled texture.");
                        return false;
                    }

                    if (texture is null)
                    {
                        diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                            "Sampled texture context field did not produce a resource binding.");
                        return false;
                    }

                    resourceBindings[field] = texture.Binding;
                    resources.Add(texture);
                    continue;
                }

                if (!TryBuildContextStorageResource(field, contextParameter, context, seenBindings,
                        structDefinitions, out var boundResource, out var boundReason, out var boundDiagnosticId))
                {
                    diagnostic = CreateDiagnostic(field, boundDiagnosticId,
                        boundReason ?? "Unsupported context descriptor resource.");
                    return false;
                }

                if (boundResource is not null)
                {
                    resourceBindings[field] = boundResource.Binding;
                    resources.Add(boundResource);
                }

                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, context.PushConstantAttributeType))
            {
                if (!TryMapShaderType(field.Type, context, structDefinitions,
                        new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var glslType,
                        out var layout, out var members, out var reason))
                {
                    diagnostic = CreateDiagnostic(field, ShaderDiagnosticId.DSH002,
                        reason ?? $"Unsupported push-constant field '{field.Name}'.");
                    return false;
                }

                pushOffset = AlignUp(pushOffset, layout.Alignment);
                pushMembers.Add(new ShaderIrStructMember
                {
                    Name = field.Name,
                    GlslName = "member_" + SanitizeName(field.Name),
                    GlslType = glslType,
                    Offset = pushOffset,
                    Alignment = layout.Alignment,
                    Size = layout.Size,
                    ArrayStride = layout.ArrayStride,
                    MatrixStride = layout.MatrixStride,
                    Members = members
                });
                pushOffset += layout.Size;
                pushAlignment = Math.Max(pushAlignment, layout.Alignment);
            }
        }

        if (pushMembers.Count > 0)
        {
            pushConstants.Add(new ShaderIrPushConstant
            {
                Name = "DeltaPushConstants",
                ParameterName = contextParameter.Name,
                GlslType = "DeltaPushConstants",
                Alignment = pushAlignment,
                Size = AlignUp(pushOffset, pushAlignment),
                Members = pushMembers
            });
        }

        return true;
    }

    private static bool TryBuildContextStorageResource(
        IFieldSymbol field,
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        out ShaderIrResource? resource,
        out string? reason,
        out string diagnosticId)
    {
        resource = null;
        reason = null;
        diagnosticId = ShaderDiagnosticId.DSH002;
        if (!TryGetBufferElementType(field.Type, context, out var elementType))
        {
            reason = $"Context field '{field.Name}' must use a typed storage-buffer wrapper.";
            return false;
        }

        if (ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out var referenceType))
        {
            reason = $"Shader-visible storage-buffer type '{elementType}' contains reference type '{referenceType}'.";
            diagnosticId = ShaderDiagnosticId.DSH010;
            return false;
        }

        var attribute = field.GetAttributes().FirstOrDefault(candidate =>
            IsLayoutAttribute(candidate.AttributeClass, context));
        if (attribute is null)
        {
            reason = $"Storage-buffer field '{field.Name}' requires an explicit binding and access contract.";
            return false;
        }

        var set = GetAttributeUIntArg(attribute, 0);
        var binding = GetAttributeUIntArg(attribute, 1);
        if (!set.HasValue || !binding.HasValue)
        {
            reason = $"Storage-buffer field '{field.Name}' requires constant set and binding arguments.";
            return false;
        }

        if (!TryMapShaderType(elementType, context, structDefinitions,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var elementGlslType,
                out var elementLayout, out var members, out reason))
        {
            diagnosticId = ShaderVisibleTypeValidation.TryFindReferenceType(elementType, out _)
                ? ShaderDiagnosticId.DSH010
                : ShaderDiagnosticId.DSH002;
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            reason = $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for context field '{field.Name}'.";
            diagnosticId = ShaderDiagnosticId.DSH005;
            return false;
        }

        var readOnly = IsReadOnlyStorageBuffer(field.Type, context);
        resource = new ShaderIrResource
        {
            Name = field.Name,
            ParameterName = contextParameter.Name + "." + field.Name,
            Category = ShaderResourceKind.StorageBuffer,
            Set = key.Set,
            Binding = key.Binding,
            GlslType = elementGlslType,
            ReadOnly = readOnly,
            Access = readOnly ? ShaderResourceAccess.ReadOnly : ShaderResourceAccess.ReadWrite,
            Std430Layout = elementLayout,
            Members = members
        };
        return true;
    }

    private static bool TryBuildContextTextureResource(
        IFieldSymbol field,
        IParameterSymbol contextParameter,
        ModuleCompilationContext context,
        HashSet<(uint Set, uint Binding)> seenBindings,
        out ShaderIrResource? resource,
        out string? reason)
    {
        resource = null;
        reason = null;
        var attribute = field.GetAttributes().FirstOrDefault(candidate =>
            IsLayoutAttribute(candidate.AttributeClass, context));
        if (attribute is null || attribute.ConstructorArguments.Length != 2)
        {
            reason = $"SampledTexture2D field '{field.Name}' requires [Layout(set, binding)].";
            return false;
        }

        var set = GetAttributeUIntArg(attribute, 0);
        var binding = GetAttributeUIntArg(attribute, 1);
        if (!set.HasValue || !binding.HasValue)
        {
            reason = $"SampledTexture2D field '{field.Name}' requires constant set and binding arguments.";
            return false;
        }

        var key = (Set: set.Value, Binding: binding.Value);
        if (!seenBindings.Add(key))
        {
            reason = $"Duplicate descriptor (set = {key.Set}, binding = {key.Binding}) detected for context field '{field.Name}'.";
            return false;
        }

        resource = new ShaderIrResource
        {
            Name = field.Name,
            ParameterName = contextParameter.Name + "." + field.Name,
            Category = ShaderResourceKind.SampledTexture2D,
            Stage = ShaderStage.Compute,
            Set = key.Set,
            Binding = key.Binding,
            GlslType = "sampler2D",
            ReadOnly = true,
            Access = ShaderResourceAccess.ReadOnly,
            Layout = "opaque"
        };
        return true;
    }

    private static bool IsContextFieldAttribute(ITypeSymbol? attributeType, ModuleCompilationContext context)
        => IsLayoutAttribute(attributeType, context) ||
           SymbolEqualityComparer.Default.Equals(attributeType, context.PushConstantAttributeType);

    private static bool TryMapShaderType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        HashSet<INamedTypeSymbol> visiting,
        out string glslType,
        out ShaderStd430Layout layout,
        out IReadOnlyList<ShaderIrStructMember> members,
        out string reason)
    {
        members = Array.Empty<ShaderIrStructMember>();
        if (ShaderEnumSupport.TryMap(type, out glslType))
        {
            layout = ShaderStd430Layout.ForGlslType(glslType);
            reason = string.Empty;
            return true;
        }

        if (context.Intrinsics.TryMapType(type, out glslType))
        {
            layout = ShaderStd430Layout.ForGlslType(glslType);
            reason = string.Empty;
            return true;
        }

        if (IsDeltaMathsHalf(type))
        {
            glslType = "float16_t";
            layout = ShaderStd430Layout.ForGlslType(glslType);
            reason = string.Empty;
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                glslType = "bool";
                break;
            case SpecialType.System_Int32:
                glslType = "int";
                break;
            case SpecialType.System_UInt32:
                glslType = "uint";
                break;
            case SpecialType.System_Single:
                glslType = "float";
                break;
            case SpecialType.System_Double:
                glslType = "double";
                break;
            default:
                if (type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Struct)
                {
                    if (!TryBuildStructLayout(namedType, context, structDefinitions, visiting, out var structure, out reason) || structure is null)
                    {
                        glslType = string.Empty;
                        layout = ShaderStd430Layout.ForGlslType("uint");
                        return false;
                    }

                    glslType = structure.GlslName;
                    layout = ShaderStd430Layout.ForStruct(structure.Alignment, structure.Size);
                    members = structure.Members;
                    return true;
                }

                glslType = string.Empty;
                layout = ShaderStd430Layout.ForGlslType("uint");
                reason = $"Unsupported shader type '{type}'. Shader records must contain only supported unmanaged scalar, vector, matrix, quaternion, or nested record fields.";
                return false;
        }

        layout = ShaderStd430Layout.ForGlslType(glslType);
        reason = string.Empty;
        return true;
    }

    private static bool IsLayoutAttribute(
        ITypeSymbol? attributeType,
        ModuleCompilationContext context)
        => SymbolEqualityComparer.Default.Equals(attributeType, context.LayoutAttributeType);

    private static bool IsReadOnlyStorageBuffer(ITypeSymbol type, ModuleCompilationContext context)
        => context.ReadOnlyStorageBufferType is not null &&
           SymbolEqualityComparer.Default.Equals((type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferType);

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

    private static bool TryBuildStructLayout(
        INamedTypeSymbol type,
        ModuleCompilationContext context,
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structDefinitions,
        HashSet<INamedTypeSymbol> visiting,
        out ShaderIrStruct? structure,
        out string reason)
    {
        if (structDefinitions.TryGetValue(type, out var existing))
        {
            structure = existing;
            reason = string.Empty;
            return true;
        }

        if (ShaderStructSupport.IsStateless(type))
        {
            structure = null;
            reason = string.Empty;
            return true;
        }

        if (!visiting.Add(type))
        {
            structure = null;
            reason = $"Recursive shader struct '{type.ToDisplayString()}' is not supported.";
            return false;
        }

        var layoutAttribute = type.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layoutAttribute is not null && layoutAttribute.ConstructorArguments.Length > 0)
        {
            var layoutKind = layoutAttribute.ConstructorArguments[0].Value;
            if (layoutKind is int kind && (kind == 2 || kind == 3))
            {
                visiting.Remove(type);
                structure = null;
                reason = $"Shader struct '{type.ToDisplayString()}' uses explicit or auto layout; only sequential layout is supported for std430 reflection.";
                return false;
            }
        }

        var members = new List<ShaderIrStructMember>();
        uint offset = 0;
        uint alignment = 1;
        foreach (var member in GetStructValueMembers(type))
        {
            var memberType = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
            if (memberType is INamedTypeSymbol nestedType &&
                nestedType.TypeKind == TypeKind.Struct &&
                !TryMapComputeType(nestedType, context, null, out _) &&
                ShaderStructSupport.IsStateless(nestedType))
            {
                continue;
            }

            if (memberType is IArrayTypeSymbol arrayType && SymbolEqualityComparer.Default.Equals(arrayType.ElementType, type))
            {
                visiting.Remove(type);
                structure = null;
                reason = $"Recursive shader struct '{type.ToDisplayString()}' through member '{member.Name}' is not supported.";
                return false;
            }

            if (!TryMapShaderType(memberType, context, structDefinitions, visiting, out var fieldGlslType, out var fieldLayout, out var nestedMembers, out reason))
            {
                visiting.Remove(type);
                structure = null;
                reason = $"Shader struct member '{type.ToDisplayString()}.{member.Name}' is unsupported: {reason}";
                return false;
            }

            offset = AlignUp(offset, fieldLayout.Alignment);
            members.Add(new ShaderIrStructMember
            {
                Name = member.Name,
                GlslName = "member_" + SanitizeName(member.Name),
                GlslType = fieldGlslType,
                Offset = offset,
                Alignment = fieldLayout.Alignment,
                Size = fieldLayout.Size,
                ArrayStride = fieldLayout.ArrayStride,
                MatrixStride = fieldLayout.MatrixStride,
                Members = nestedMembers
            });
            offset += fieldLayout.Size;
            alignment = Math.Max(alignment, fieldLayout.Alignment);
        }

        if (members.Count == 0)
        {
            visiting.Remove(type);
            structure = null;
            reason = $"Shader struct '{type.ToDisplayString()}' has no instance data fields.";
            return false;
        }

        var size = AlignUp(offset, alignment);
        structure = new ShaderIrStruct
        {
            Name = type.ToDisplayString(),
            GlslName = "DeltaStruct_" + SanitizeName(type.ToDisplayString()),
            Alignment = alignment,
            Size = size,
            ArrayStride = size,
            Members = members
        };
        structDefinitions[type] = structure;
        visiting.Remove(type);
        reason = string.Empty;
        return true;
    }

    private static uint AlignUp(uint value, uint alignment)
        => alignment == 0 ? value : (value + alignment - 1) / alignment * alignment;

    private static bool TryGetBufferElementType(
        ITypeSymbol type,
        ModuleCompilationContext context,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        elementType = null;

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var originalDefinition = namedType.OriginalDefinition;
        if (context.ReadOnlyStorageBufferType is null || context.ReadWriteStorageBufferType is null ||
            (!SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadOnlyStorageBufferType) &&
             !SymbolEqualityComparer.Default.Equals(originalDefinition, context.ReadWriteStorageBufferType)))
        {
            return false;
        }

        if (namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = namedType.TypeArguments[0];
        return true;
    }

    private static uint? GetAttributeUIntArg(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index)
        {
            return null;
        }

        var value = attribute.ConstructorArguments[index];
        return value.Value is uint uintValue ? uintValue : value.Value is int intValue ? (uint)intValue : null;
    }

    private static bool ValidateProfileCompatibility(ShaderCompilationOptions options, out string? reason)
    {
        reason = null;

        if (!TryParseProfileVersion(options.Profile, out var profileVersion))
        {
            reason = $"Unsupported profile '{options.Profile}'.";
            return false;
        }

        if (!string.Equals(options.Glsl, "460", StringComparison.Ordinal))
        {
            reason = $"Only Vulkan GLSL 460 is supported; received GLSL '{options.Glsl}'.";
            return false;
        }

        if (!Version.TryParse(options.Spirv, out var spirvVersion))
        {
            reason = $"Unsupported SPIR-V version '{options.Spirv}'.";
            return false;
        }

        var maxSpirv = profileVersion >= new Version(1, 3) ? new Version(1, 6) : new Version(1, 5);
        if (profileVersion > new Version(1, 3))
        {
            reason = $"Profile '{options.Profile}' requires additional validation not implemented in this compiler version.";
            return false;
        }

        if (spirvVersion > maxSpirv || spirvVersion < new Version(1, 0))
        {
            reason = $"Profile '{options.Profile}' is incompatible with SPIR-V '{options.Spirv}'. Maximum supported SPIR-V for this profile is {maxSpirv}.";
            return false;
        }

        return true;
    }

    private static bool TryValidateLocalSize(
        ShaderEntryPointSymbol entry,
        ShaderCompilationOptions options,
        out string? error)
    {
        error = null;
        if (!TryParseProfileVersion(options.Profile, out var profileVersion))
        {
            error = $"Unable to validate local size for unsupported profile '{options.Profile}'.";
            return false;
        }

        var profile = profileVersion >= new Version(1, 3) ? 1.3m : 1.2m;
        var maxX = 1024u;
        var maxY = profile >= 1.3m ? 1024u : 1024u;
        var maxZ = profile >= 1.2m ? 64u : 1u;
        var maxInvocations = 1024u;

        if (entry.LocalSizeX == 0 || entry.LocalSizeY == 0 || entry.LocalSizeZ == 0)
        {
            error = "Compute local size dimensions must be positive non-zero values.";
            return false;
        }

        if (entry.LocalSizeX > maxX || entry.LocalSizeY > maxY)
        {
            error = $"Compute local size exceeded target limit: x <= {maxX}, y <= {maxY}.";
            return false;
        }

        if (entry.LocalSizeZ > maxZ)
        {
            error = $"Compute local size exceeded target limit: z <= {maxZ}.";
            return false;
        }

        var invocations = (ulong)entry.LocalSizeX * entry.LocalSizeY * entry.LocalSizeZ;
        if (invocations > maxInvocations)
        {
            error = $"Compute local_size total invocations must not exceed {maxInvocations} for current target profile.";
            return false;
        }

        return true;
    }

    private static bool TryParseProfileVersion(string profile, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        var trimmed = profile.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var number = trimmed.Substring("vulkan".Length);
        if (string.IsNullOrWhiteSpace(number))
        {
            return false;
        }

        if (number.StartsWith(".", StringComparison.Ordinal))
        {
            number = number.TrimStart('.');
        }

        if (number.StartsWith("_"))
        {
            number = number.Substring(1);
        }

        if (!Version.TryParse(number, out version))
        {
            return false;
        }

        return true;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "resource";
        }

        return new string(name
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray());
    }
}

public sealed class ModuleCompilationContext
{
    public ModuleCompilationContext(Compilation compilation)
        : this(compilation, IntrinsicRegistry.Build(compilation))
    {
    }

    public ModuleCompilationContext(Compilation compilation, IntrinsicRegistry intrinsics)
    {
        Compilation = compilation;
        Intrinsics = intrinsics;
        ReadOnlyStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.ReadOnlyStorageBuffer`1");
        ReadWriteStorageBufferType = compilation.GetTypeByMetadataName("Delta.Shader.ReadWriteStorageBuffer`1");
        SampledTexture2DType = compilation.GetTypeByMetadataName("Delta.Shader.SampledTexture2D");
        LayoutAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.LayoutAttribute");
        InterstageAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.InterstageAttribute");
        PushConstantAttributeType = compilation.GetTypeByMetadataName("Delta.Shader.PushConstantAttribute");
        SemanticValueFields = BuildSemanticValueFields(compilation);
    }

    public Compilation Compilation { get; }
    public IntrinsicRegistry Intrinsics { get; }
    public ITypeSymbol? ReadOnlyStorageBufferType { get; }
    public ITypeSymbol? ReadWriteStorageBufferType { get; }
    public ITypeSymbol? SampledTexture2DType { get; }
    public ITypeSymbol? LayoutAttributeType { get; }
    public ITypeSymbol? InterstageAttributeType { get; }
    public ITypeSymbol? PushConstantAttributeType { get; }
    public IReadOnlyDictionary<INamedTypeSymbol, IFieldSymbol> SemanticValueFields { get; }

    private static IReadOnlyDictionary<INamedTypeSymbol, IFieldSymbol> BuildSemanticValueFields(Compilation compilation)
    {
        var fields = new Dictionary<INamedTypeSymbol, IFieldSymbol>(SymbolEqualityComparer.Default);
        string[] semanticTypeNames =
        [
            "Delta.Shader.Position",
            "Delta.Shader.Uv0",
            "Delta.Shader.Uv1",
            "Delta.Shader.Color",
            "Delta.Shader.VertexColor",
            "Delta.Shader.FragmentColor",
            "Delta.Shader.WorldPosition",
            "Delta.Shader.WorldNormal",
            "Delta.Shader.Tangent",
            "Delta.Shader.Pixel",
            "Delta.Shader.SegmentRect",
            "Delta.Shader.CornerData",
            "Delta.Shader.CornerRadii",
            "Delta.Shader.BorderWidth"
        ];

        foreach (var typeName in semanticTypeNames)
        {
            if (compilation.GetTypeByMetadataName(typeName) is not INamedTypeSymbol type ||
                type.GetMembers("Value").OfType<IFieldSymbol>().SingleOrDefault() is not IFieldSymbol valueField)
            {
                continue;
            }

            fields[type] = valueField;
        }

        return fields;
    }
}
