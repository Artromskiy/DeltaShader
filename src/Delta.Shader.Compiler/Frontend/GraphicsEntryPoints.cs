using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Compiler.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.Shader.Compiler;

internal static class GraphicsEntryPoints
{
    public static ShaderCompilationResult ValidateAndBuild(
        ModuleCompilationContext context,
        RoslynFrontend frontend,
        ShaderStage stage,
        ShaderCompilationOptions? options = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<ShaderDiagnostic>();
        var entries = frontend.FindShaderEntryPoints().Where(entry => entry.Stage == stage).ToArray();
        if (entries.Length == 0)
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH004,
                $"No valid [{stage}Shader] entry point found.", Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(string.Empty, false, diagnostics);
        }

        if (entries.Length > 1)
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH004,
                $"Only one [{stage}Shader] entry point is supported per module.", Severity: ShaderDiagnosticSeverity.Error));
        }

        var entry = entries[0];
        if (!entry.Method.IsStatic || !entry.Method.ReturnsVoid)
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH004,
                $"[{stage}Shader] entry point must be static void.", Severity: ShaderDiagnosticSeverity.Error));
        }

        var inputs = new List<ShaderIrInterfaceVariable>();
        var outputs = new List<ShaderIrInterfaceVariable>();
        var pushConstants = new List<ShaderIrPushConstant>();
        var structures = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);
        var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        var pushFieldMap = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var parameter in entry.Method.Parameters)
        {
            var attribute = parameter.GetAttributes().FirstOrDefault();
            var attributeType = attribute?.AttributeClass;
            var location = parameter.Locations.FirstOrDefault()?.GetLineSpan();

            if (Same(attributeType, context.VertexIndexAttributeType))
            {
                if (stage != ShaderStage.Vertex || parameter.Type.SpecialType != SpecialType.System_UInt32 || parameter.RefKind != RefKind.None)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011, "[VertexIndex] is only valid on a value uint parameter of a vertex shader.", location);
                }
                else
                {
                    parameterMap[parameter] = "uint(gl_VertexIndex)";
                    inputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "uint", GlslName = "gl_VertexIndex", Builtin = "VertexIndex" });
                }
                continue;
            }

            if (Same(attributeType, context.FragmentCoordAttributeType))
            {
                var coordType = context.Intrinsics.TryMapType(parameter.Type, out var mappedCoordType) ? mappedCoordType : string.Empty;
                if (stage != ShaderStage.Fragment || !string.Equals(coordType, "vec2", StringComparison.Ordinal) || parameter.RefKind != RefKind.None)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011, "[FragmentCoord] is only valid on a float2 value parameter of a fragment shader.", location);
                }
                else
                {
                    parameterMap[parameter] = "gl_FragCoord.xy";
                    inputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "vec2", GlslName = "gl_FragCoord", Builtin = "FragmentCoord" });
                }
                continue;
            }

            if (Same(attributeType, context.PositionAttributeType))
            {
                if (stage != ShaderStage.Vertex || parameter.RefKind != RefKind.Out || !TryMapType(parameter.Type, context, out var positionType) || positionType != "vec4")
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "[Position] is only valid on an out float4 vertex parameter.", location);
                }
                else
                {
                    parameterMap[parameter] = "gl_Position";
                    outputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "vec4", GlslName = "gl_Position", Builtin = "Position" });
                }
                continue;
            }

            if (Same(attributeType, context.FragmentColorAttributeType))
            {
                if (stage != ShaderStage.Fragment || parameter.RefKind != RefKind.Out || !TryMapType(parameter.Type, context, out var colorType) || colorType != "vec4")
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "[FragmentColor] is only valid on an out float4 fragment parameter.", location);
                }
                else
                {
                    parameterMap[parameter] = "fragColor";
                    outputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "vec4", GlslName = "fragColor", Location = 0, Builtin = "FragmentColor" });
                }
                continue;
            }

            if (Same(attributeType, context.ShaderVaryingAttributeType))
            {
                var varyingLocation = GetUIntArg(attribute!, 0);
                if (!TryMapType(parameter.Type, context, out var varyingType) || varyingType is not ("vec2" or "vec3" or "vec4") ||
                    (stage == ShaderStage.Vertex && parameter.RefKind != RefKind.Out) ||
                    (stage == ShaderStage.Fragment && parameter.RefKind != RefKind.None))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "Shader varyings must be vertex out or fragment value vector parameters.", location);
                }
                else
                {
                    var glslName = "varying_" + varyingLocation;
                    parameterMap[parameter] = glslName;
                    var variable = new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = varyingType, GlslName = glslName, Location = varyingLocation };
                    (stage == ShaderStage.Vertex ? outputs : inputs).Add(variable);
                }
                continue;
            }

            if (Same(attributeType, context.PushConstantAttributeType))
            {
                var namedType = parameter.Type as INamedTypeSymbol;
                if (parameter.RefKind != RefKind.None || namedType is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006, "Push constant parameters must be sequential shader structs.", location);
                }
                else if (!TryBuildStruct(namedType, context, structures, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var pushStruct, out var pushReason))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006, pushReason ?? "Push constant parameters must be sequential shader structs.", location);
                }
                else
                {
                    var push = new ShaderIrPushConstant
                    {
                        Name = "DeltaPushConstants",
                        ParameterName = parameter.Name,
                        GlslType = pushStruct!.GlslName,
                        Alignment = pushStruct.Alignment,
                        Size = pushStruct.Size,
                        ArrayStride = pushStruct.ArrayStride,
                        Members = pushStruct.Members
                    };
                    pushConstants.Add(push);
                    parameterMap[parameter] = "pushConstants";
                    foreach (var field in namedType!.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                    {
                        var member = pushStruct.Members.FirstOrDefault(candidate => candidate.Name == field.Name);
                        if (member is not null)
                        {
                            pushFieldMap[field] = "pushConstants." + member.GlslName;
                        }
                    }
                    structures.Remove(namedType);
                }
                continue;
            }

            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                $"Graphics entry point parameter '{parameter.Name}' is not a supported stage builtin, varying, or push constant.", location);
        }

        if (stage == ShaderStage.Vertex && outputs.All(output => output.Builtin != "Position"))
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH012, "Vertex shader must declare one [Position] output.", Severity: ShaderDiagnosticSeverity.Error));
        }
        if (stage == ShaderStage.Fragment && outputs.All(output => output.Builtin != "FragmentColor"))
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH012, "Fragment shader must declare one [FragmentColor] output.", Severity: ShaderDiagnosticSeverity.Error));
        }

        string body = string.Empty;
        if (diagnostics.Count == 0)
        {
            var syntax = entry.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (syntax?.Body is null)
            {
                diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, "Graphics shader entry point body is required.", Severity: ShaderDiagnosticSeverity.Error));
            }
            else
            {
                var semanticModel = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
                if (!GraphicsShaderBodyTranslator.TryTranslate(syntax.Body, semanticModel, context, stage, parameterMap, pushFieldMap, out body, out var reason))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, reason!, Severity: ShaderDiagnosticSeverity.Error));
                }
            }
        }

        var module = new ShaderIrModule
        {
            Stage = stage,
            SourceEntryPointName = entry.Name,
            EntryPointName = entry.Name,
            Resources = [],
            Structs = structures.Values.OrderBy(structure => structure.GlslName, StringComparer.Ordinal).ToArray(),
            Requirements = [$"Vulkan {resultOptions.Profile}", $"GLSL {resultOptions.Glsl}", $"SPIRV {resultOptions.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
            Body = body,
            Inputs = inputs,
            Outputs = outputs,
            PushConstants = pushConstants
        };
        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, resultOptions);
    }

    private static void AddDiagnostic(List<ShaderDiagnostic> diagnostics, string id, string message, FileLinePositionSpan? location)
        => diagnostics.Add(new ShaderDiagnostic(id, message, location?.Path, location is null ? 0 : location.Value.StartLinePosition.Line + 1, location is null ? 0 : location.Value.StartLinePosition.Character + 1));

    private static bool Same(ITypeSymbol? left, ITypeSymbol? right)
        => left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);

    private static uint GetUIntArg(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is not null
            ? Convert.ToUInt32(attribute.ConstructorArguments[index].Value)
            : 0;

    private static bool TryMapType(ITypeSymbol type, ModuleCompilationContext context, out string glslType)
    {
        if (context.Intrinsics.TryMapType(type, out glslType)) return true;
        glslType = type.SpecialType switch
        {
            SpecialType.System_Single => "float",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Boolean => "bool",
            _ => string.Empty
        };
        return glslType.Length != 0;
    }

    private static bool TryBuildStruct(INamedTypeSymbol type, ModuleCompilationContext context, Dictionary<INamedTypeSymbol, ShaderIrStruct> definitions, HashSet<INamedTypeSymbol> visiting, out ShaderIrStruct structure, out string? reason)
    {
        if (definitions.TryGetValue(type, out structure!)) { reason = null; return true; }
        if (!visiting.Add(type)) { structure = default!; reason = $"Recursive shader struct '{type.ToDisplayString()}' is not supported."; return false; }
        var layout = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layout?.ConstructorArguments.FirstOrDefault().Value is int kind && (kind == 2 || kind == 3))
        { visiting.Remove(type); structure = default!; reason = $"Shader struct '{type.ToDisplayString()}' uses explicit or auto layout."; return false; }
        var members = new List<ShaderIrStructMember>();
        uint offset = 0, alignment = 1;
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            if (!TryMapType(field.Type, context, out var glslType) && field.Type is INamedTypeSymbol nested && nested.TypeKind == TypeKind.Struct)
            {
                if (!TryBuildStruct(nested, context, definitions, visiting, out var nestedStruct, out reason)) { structure = default!; visiting.Remove(type); return false; }
                glslType = nestedStruct.GlslName;
                var nestedLayout = ShaderStd430Layout.ForStruct(nestedStruct.Alignment, nestedStruct.Size);
                offset = AlignUp(offset, nestedLayout.Alignment);
                members.Add(new ShaderIrStructMember { Name = field.Name, GlslName = "member_" + Sanitize(field.Name), GlslType = glslType, Offset = offset, Alignment = nestedLayout.Alignment, Size = nestedLayout.Size, ArrayStride = nestedLayout.ArrayStride, Members = nestedStruct.Members });
                offset += nestedLayout.Size; alignment = Math.Max(alignment, nestedLayout.Alignment); continue;
            }
            if (string.IsNullOrEmpty(glslType)) { structure = default!; visiting.Remove(type); reason = $"Shader struct field '{field.Name}' has unsupported type '{field.Type}'."; return false; }
            var fieldLayout = ShaderStd430Layout.ForGlslType(glslType);
            offset = AlignUp(offset, fieldLayout.Alignment);
            members.Add(new ShaderIrStructMember { Name = field.Name, GlslName = "member_" + Sanitize(field.Name), GlslType = glslType, Offset = offset, Alignment = fieldLayout.Alignment, Size = fieldLayout.Size, ArrayStride = fieldLayout.ArrayStride, MatrixStride = fieldLayout.MatrixStride });
            offset += fieldLayout.Size; alignment = Math.Max(alignment, fieldLayout.Alignment);
        }
        if (members.Count == 0) { structure = default!; visiting.Remove(type); reason = $"Shader struct '{type.ToDisplayString()}' has no instance data fields."; return false; }
        structure = new ShaderIrStruct { Name = type.ToDisplayString(), GlslName = "DeltaStruct_" + Sanitize(type.ToDisplayString()), Alignment = alignment, Size = AlignUp(offset, alignment), ArrayStride = AlignUp(offset, alignment), Members = members };
        definitions[type] = structure; visiting.Remove(type); reason = null; return true;
    }

    private static uint AlignUp(uint value, uint alignment) => alignment == 0 ? value : (value + alignment - 1) / alignment * alignment;
    private static string Sanitize(string value) => new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
}

internal static class GraphicsShaderBodyTranslator
{
    public static bool TryTranslate(BlockSyntax body, SemanticModel model, ModuleCompilationContext context, ShaderStage stage, IReadOnlyDictionary<IParameterSymbol, string> parameterMap, IReadOnlyDictionary<IFieldSymbol, string> pushFieldMap, out string translated, out string? reason)
    {
        var rewriter = new Rewriter(model, context, stage, parameterMap, pushFieldMap);
        var rewritten = rewriter.Visit(body);
        translated = rewritten?.ToFullString().Trim() ?? string.Empty;
        foreach (var field in pushFieldMap)
        {
            foreach (var parameter in parameterMap.Keys.Where(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, field.Key.ContainingType)))
            {
                translated = translated.Replace(parameter.Name + "." + field.Key.Name, field.Value);
            }
        }
        foreach (var parameter in parameterMap)
            translated = Regex.Replace(translated, $"\\b{Regex.Escape(parameter.Key.Name)}\\b", parameter.Value, RegexOptions.None);
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (_TryBinding(model, context, invocation, stage, out var glslName) && glslName is not null)
                translated = translated.Replace(invocation.Expression.ToString(), glslName);
        }
        foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var type = model.GetTypeInfo(creation).Type;
            if (type is not null && context.Intrinsics.TryMapType(type, out var glslType))
                translated = translated.Replace("new " + creation.Type.ToString(), glslType);
        }
        foreach (var declaration in body.DescendantNodes().OfType<VariableDeclarationSyntax>().Where(declaration => declaration.Type.IsVar && declaration.Variables.Count == 1))
        {
            var type = declaration.Variables[0].Initializer is { } initializer
                ? model.GetTypeInfo(initializer.Value).Type
                : null;
            if (type is not null && context.Intrinsics.TryMapType(type, out var glslType))
                translated = Regex.Replace(translated, $"\\b{Regex.Escape(glslType)}(?=[A-Za-z_]\\w*\\s*=)", glslType + " ", RegexOptions.None);
        }
        translated = translated.Replace(";", ";\n").Replace("\r\n", "\n").Replace("\r", "\n");
        translated = Regex.Replace(translated, @"\b(vec[234]|ivec[234]|uvec[234]|bvec[234]|mat[234]|float|int|uint|bool)([A-Za-z_]\w*)\s*=", "$1 $2 =", RegexOptions.None);
        translated = System.Text.RegularExpressions.Regex.Replace(translated, @"(?<=\d)f\b", string.Empty);
        reason = rewriter.Reason;
        return reason is null;
    }

    private static bool _TryBinding(SemanticModel model, ModuleCompilationContext context, InvocationExpressionSyntax invocation, ShaderStage stage, out string? glslName)
    {
        glslName = null;
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !context.Intrinsics.TryGetIntrinsic(method, out var binding)) return false;
        if (!binding.SupportsStage(stage)) return false;
        if (binding.GlslName is "*" or "/" or "+" or "-") return false;
        glslName = binding.GlslName;
        return true;
    }

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly ModuleCompilationContext _context;
        private readonly ShaderStage _stage;
        private readonly IReadOnlyDictionary<IParameterSymbol, string> _parameters;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _pushFields;
        public string? Reason { get; private set; }

        public Rewriter(SemanticModel model, ModuleCompilationContext context, ShaderStage stage, IReadOnlyDictionary<IParameterSymbol, string> parameters, IReadOnlyDictionary<IFieldSymbol, string> pushFields)
        { _model = model; _context = context; _stage = stage; _parameters = parameters; _pushFields = pushFields; }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is IParameterSymbol parameter && _parameters.TryGetValue(parameter, out var parameterName)) return SyntaxFactory.ParseName(parameterName);
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is IFieldSymbol field && _pushFields.TryGetValue(field, out var fieldName)) return SyntaxFactory.ParseExpression(fieldName);
            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var args = node.ArgumentList.Arguments.Select(argument => Visit(argument.Expression)!).ToArray();
            if (symbol is not null && _context.Intrinsics.TryGetIntrinsic(symbol, out var binding))
            {
                if (!binding.SupportsStage(_stage))
                { Reason ??= $"Intrinsic '{symbol.Name}' is not valid in {_stage} stage."; }
                if (binding.GlslName is "*" or "/" or "+" or "-") return base.VisitInvocationExpression(node);
                return SyntaxFactory.ParseExpression(binding.GlslName + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var type = _model.GetTypeInfo(node).Type;
            if (type is not null && _context.Intrinsics.TryMapType(type, out var glslType))
            {
                var args = node.ArgumentList?.Arguments.Select(argument => Visit(argument.Expression)!).ToArray() ?? Array.Empty<ExpressionSyntax>();
                return SyntaxFactory.ParseExpression(glslType + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            return base.VisitObjectCreationExpression(node);
        }

        public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            if (node.Type.IsVar && node.Variables.Count == 1)
            {
                var type = node.Variables[0].Initializer is { } initializer
                    ? _model.GetTypeInfo(initializer.Value).Type
                    : null;
                if (type is not null && TryMap(type, out var glslType)) return node.WithType(SyntaxFactory.ParseTypeName(glslType));
            }
            return base.VisitVariableDeclaration(node);
        }

        private bool TryMap(ITypeSymbol type, out string glslType)
        {
            if (_context.Intrinsics.TryMapType(type, out glslType)) return true;
            glslType = type.SpecialType switch { SpecialType.System_Single => "float", SpecialType.System_UInt32 => "uint", SpecialType.System_Int32 => "int", _ => string.Empty };
            return glslType.Length > 0;
        }
    }
}
