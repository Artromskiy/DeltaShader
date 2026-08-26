using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Delta.Shader;
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
        ShaderCompilationOptions? options = null,
        string? entryPointName = null,
        string? entryPointIdentity = null)
    {
        var resultOptions = options ?? ShaderCompilationOptions.Default;
        var diagnostics = new List<ShaderDiagnostic>();
        var entries = frontend.FindShaderEntryPoints()
            .Where(entry => entry.Stage == stage && (entryPointName is null || entry.Method.Name == entryPointName) &&
                (entryPointIdentity is null || entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == entryPointIdentity))
            .ToArray();
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
        var vertexInputs = new List<ShaderIrVertexInput>();
        var vertexBuffers = new List<ShaderIrVertexBufferBinding>();
        var outputs = new List<ShaderIrInterfaceVariable>();
        var pushConstants = new List<ShaderIrPushConstant>();
        var resources = new List<ShaderIrResource>();
        var storageBufferTargets = new HashSet<string>(StringComparer.Ordinal);
        var seenBindings = new HashSet<(uint Set, uint Binding)>();
        var structures = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);
        var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        var pushFieldMap = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var parameter in entry.Method.Parameters)
        {
            var visibleType = ShaderVisibleTypeValidation.GetVisibleRootType(parameter, context.Compilation);
            var visibleTypeIssues = ShaderVisibleTypeValidation.Validate(visibleType, parameter);
            foreach (var issue in visibleTypeIssues)
            {
                AddDiagnostic(diagnostics, issue.Id, issue.Message, issue.Symbol.Locations.FirstOrDefault()?.GetLineSpan());
            }

            if (visibleTypeIssues.Count > 0)
            {
                continue;
            }

            var attribute = parameter.GetAttributes().FirstOrDefault();
            var attributeType = attribute?.AttributeClass;
            var locationSpan = parameter.Locations.FirstOrDefault()?.GetLineSpan();

            if (Same(attributeType, context.VertexIndexAttributeType))
            {
                if (stage != ShaderStage.Vertex || parameter.Type.SpecialType != SpecialType.System_UInt32 || parameter.RefKind != RefKind.None)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011, "[VertexIndex] is only valid on a value uint parameter of a vertex shader.", locationSpan);
                }
                else
                {
                    parameterMap[parameter] = "uint(gl_VertexIndex)";
                    inputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "uint", GlslName = "gl_VertexIndex", Builtin = "VertexIndex" });
                }
                continue;
            }

            if (Same(attributeType, context.VertexInputAttributeType) && attribute is not null)
            {
                var vertexLocation = GetUIntArg(attribute, 0);
                var vertexBinding = GetUIntNamedArg(attribute, "Binding");
                var byteOffset = GetUIntNamedArg(attribute, "ByteOffset");
                var inputRate = GetInputRate(attribute);
                if (stage != ShaderStage.Vertex || parameter.RefKind != RefKind.None ||
                    !TryMapType(parameter.Type, context, out var vertexType) ||
                    !TryGetVertexInputLayout(vertexType, out var byteSize, out var alignment, out var formatHint))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        "[VertexInput] is only valid on a value vertex-stage parameter with a supported scalar or vector type.", locationSpan);
                }
                else if (vertexInputs.Any(input => input.Location == vertexLocation))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex input location {vertexLocation} is declared more than once.", locationSpan);
                }
                else if (vertexInputs.Any(input => input.Binding == vertexBinding && input.ByteOffset == byteOffset))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex buffer binding {vertexBinding} offset {byteOffset} overlaps with another vertex input.", locationSpan);
                }
                else
                {
                    var glslName = Sanitize(parameter.Name);
                    parameterMap[parameter] = glslName;
                    vertexInputs.Add(new ShaderIrVertexInput
                    {
                        Name = parameter.Name,
                        ParameterName = parameter.Name,
                        GlslName = glslName,
                        GlslType = vertexType,
                        Location = vertexLocation,
                        Binding = vertexBinding,
                        ByteOffset = byteOffset,
                        InputRate = inputRate,
                        ByteSize = byteSize,
                        Alignment = alignment,
                        FormatHint = formatHint
                    });
                }
                continue;
            }

            if (Same(attributeType, context.InstanceIndexAttributeType))
            {
                if (stage != ShaderStage.Vertex || parameter.Type.SpecialType != SpecialType.System_UInt32 || parameter.RefKind != RefKind.None)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011, "[InstanceIndex] is only valid on a value uint parameter of a vertex shader.", locationSpan);
                }
                else
                {
                    parameterMap[parameter] = "uint(gl_InstanceIndex)";
                    inputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "uint", GlslName = "gl_InstanceIndex", Builtin = "InstanceIndex" });
                }
                continue;
            }

            if (Same(attributeType, context.FragmentCoordAttributeType))
            {
                var coordType = context.Intrinsics.TryMapType(parameter.Type, out var mappedCoordType) ? mappedCoordType : string.Empty;
                if (stage != ShaderStage.Fragment || !string.Equals(coordType, "vec2", StringComparison.Ordinal) || parameter.RefKind != RefKind.None)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011, "[FragmentCoord] is only valid on a float2 value parameter of a fragment shader.", locationSpan);
                }
                else
                {
                    parameterMap[parameter] = "gl_FragCoord.xy";
                    inputs.Add(new ShaderIrInterfaceVariable { Name = parameter.Name, ParameterName = parameter.Name, GlslType = "vec2", GlslName = "gl_FragCoord", Builtin = "FragmentCoord" });
                }
                continue;
            }

            if (context.ReadOnlyStorageBufferType is not null &&
                SymbolEqualityComparer.Default.Equals((parameter.Type as INamedTypeSymbol)?.OriginalDefinition, context.ReadOnlyStorageBufferType))
            {
                if (!Same(attributeType, context.ReadOnlyStorageBufferAttributeType) || attribute is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                        $"Storage-buffer parameter '{parameter.Name}' requires [ReadOnlyStorageBuffer(set, binding)].", locationSpan);
                }
                else if (stage != ShaderStage.Vertex && stage != ShaderStage.Fragment)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011,
                        $"Storage-buffer parameter '{parameter.Name}' is only supported in vertex and fragment stages.", locationSpan);
                }
                else
                {
                    var set = GetUIntArg(attribute, 0);
                    var binding = GetUIntArg(attribute, 1);
                    if (!seenBindings.Add((set, binding)))
                    {
                        AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH005,
                            $"Graphics resources cannot share set {set}, binding {binding}.", locationSpan);
                    }
                    else if (parameter.Type is INamedTypeSymbol namedType &&
                        namedType.TypeArguments.Length == 1 &&
                        namedType.TypeArguments[0] is INamedTypeSymbol elementType &&
                        TryBuildStruct(elementType, context, structures, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var elementStruct, out _) &&
                        elementStruct is not null)
                    {
                        resources.Add(new ShaderIrResource
                        {
                            Name = parameter.Name,
                            ParameterName = parameter.Name,
                            Category = ShaderResourceKind.StorageBuffer,
                            Stage = stage,
                            Set = set,
                            Binding = binding,
                            GlslType = elementStruct.GlslName,
                            ReadOnly = true,
                            Access = ShaderResourceAccess.ReadOnly,
                            Layout = ShaderStd430Layout.Standard,
                            Std430Layout = ShaderStd430Layout.ForStruct(elementStruct.Alignment, elementStruct.Size),
                            Members = elementStruct.Members
                        });
                        storageBufferTargets.Add(parameter.Name);
                        parameterMap[parameter] = parameter.Name;
                    }
                    else
                    {
                        AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006,
                            $"Storage-buffer parameter '{parameter.Name}' must wrap a sequential shader struct value.", locationSpan);
                    }
                }
                continue;
            }

            if (Same(attributeType, context.PositionAttributeType))
            {
                if (stage != ShaderStage.Vertex || parameter.RefKind != RefKind.Out || !TryMapType(parameter.Type, context, out var positionType) || positionType != "vec4")
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "[Position] is only valid on an out float4 vertex parameter.", locationSpan);
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
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "[FragmentColor] is only valid on an out float4 fragment parameter.", locationSpan);
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
                if (attribute is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "[ShaderVarying] requires a valid location argument.", locationSpan);
                    continue;
                }

                var varyingLocation = GetUIntArg(attribute, 0);
                if (!TryMapType(parameter.Type, context, out var varyingType) || varyingType is not ("vec2" or "vec3" or "vec4") ||
                    (stage == ShaderStage.Vertex && parameter.RefKind != RefKind.Out) ||
                    (stage == ShaderStage.Fragment && parameter.RefKind != RefKind.None))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012, "Shader varyings must be vertex out or fragment value vector parameters.", locationSpan);
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
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006, "Push constant parameters must be sequential shader structs.", locationSpan);
                }
                else if (!TryBuildStruct(namedType, context, structures, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var pushStruct, out var pushReason) || pushStruct is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006, pushReason ?? "Push constant parameters must be sequential shader structs.", locationSpan);
                }
                else
                {
                    var push = new ShaderIrPushConstant
                    {
                        Name = "DeltaPushConstants",
                        ParameterName = parameter.Name,
                        GlslType = pushStruct.GlslName,
                        Alignment = pushStruct.Alignment,
                        Size = pushStruct.Size,
                        ArrayStride = pushStruct.ArrayStride,
                        Members = pushStruct.Members
                    };
                    pushConstants.Add(push);
                    parameterMap[parameter] = "pushConstants";
                    foreach (var field in namedType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
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

            if (context.SampledTexture2DType is not null &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, context.SampledTexture2DType))
            {
                if (!Same(attributeType, context.SampledTexture2DAttributeType) || attribute is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                        $"SampledTexture2D parameter '{parameter.Name}' requires [SampledTexture2D(set, binding)].", locationSpan);
                }
                else if (!SupportsStage(attribute, stage))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH011,
                        $"SampledTexture2D parameter '{parameter.Name}' is not enabled for the {stage} stage.", locationSpan);
                }
                else
                {
                    var set = GetUIntArg(attribute, 0);
                    var binding = GetUIntArg(attribute, 1);
                    if (!seenBindings.Add((set, binding)))
                    {
                        AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH005,
                            $"Graphics resources cannot share set {set}, binding {binding}.", locationSpan);
                    }
                    else
                    {
                        parameterMap[parameter] = parameter.Name;
                        resources.Add(new ShaderIrResource
                        {
                            Name = parameter.Name,
                            ParameterName = parameter.Name,
                            Category = ShaderResourceKind.SampledTexture2D,
                            Stage = stage,
                            Set = set,
                            Binding = binding,
                            GlslType = "sampler2D",
                            ReadOnly = true,
                            Access = ShaderResourceAccess.ReadOnly,
                            Layout = "opaque"
                        });
                    }
                }
                continue;
            }

            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                $"Graphics entry point parameter '{parameter.Name}' is not a supported stage builtin, varying, or push constant.", locationSpan);
        }

        if (stage == ShaderStage.Vertex && outputs.All(output => output.Builtin != "Position"))
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH012, "Vertex shader must declare one [Position] output.", Severity: ShaderDiagnosticSeverity.Error));
        }
        if (stage == ShaderStage.Fragment && outputs.All(output => output.Builtin != "FragmentColor"))
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH012, "Fragment shader must declare one [FragmentColor] output.", Severity: ShaderDiagnosticSeverity.Error));
        }

        if (stage == ShaderStage.Vertex && vertexInputs.Count > 0)
        {
            foreach (var group in vertexInputs.GroupBy(input => input.Binding))
            {
                var ordered = group.OrderBy(input => input.ByteOffset).ToArray();
                var stride = 0u;
                var rate = ordered[0].InputRate;
                foreach (var input in ordered)
                {
                    if (input.InputRate != rate)
                    {
                        diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH013,
                            $"Vertex buffer binding {group.Key} mixes input rates.", Severity: ShaderDiagnosticSeverity.Error));
                        break;
                    }

                    stride = Math.Max(stride, input.ByteOffset + input.ByteSize);
                }

                if (group.Any(input => input.ByteSize == 0))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH013,
                        $"Vertex buffer binding {group.Key} has an input with missing size.", Severity: ShaderDiagnosticSeverity.Error));
                }

                vertexBuffers.Add(new ShaderIrVertexBufferBinding
                {
                    Binding = group.Key,
                    Stride = AlignUp(stride, 4),
                    InputRate = rate,
                    Attributes = ordered
                });
            }
        }

        string body = string.Empty;
        IReadOnlyList<string> helperFunctions = [];
        IReadOnlyDictionary<IMethodSymbol, string> helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        if (diagnostics.Count == 0)
        {
            var structNames = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
            foreach (var definition in structures)
            {
                structNames[definition.Key] = definition.Value.GlslName;
            }
            var structFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
            foreach (var definition in structures)
            {
                foreach (var field in definition.Key.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                {
                    var member = definition.Value.Members.FirstOrDefault(candidate => candidate.Name == field.Name);
                    if (member is not null)
                    {
                        structFields[field] = member.GlslName;
                    }
                }
            }

            var syntax = entry.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            if (syntax?.Body is null)
            {
                diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, "Graphics shader entry point body is required.", Severity: ShaderDiagnosticSeverity.Error));
            }
            else
            {
                var semanticModel = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
                if (!TryBuildHelpers(syntax, semanticModel, context, stage, pushFieldMap, structNames, structFields, storageBufferTargets, out helperFunctions, out helperNames, out var helperReason))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, helperReason ?? "Unable to lower shader helper call graph.", Severity: ShaderDiagnosticSeverity.Error));
                }
                else if (!GraphicsShaderBodyTranslator.TryTranslate(syntax.Body, semanticModel, context, stage, parameterMap, pushFieldMap, structNames, structFields, storageBufferTargets, helperNames, out body, out var reason))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, reason ?? "Unable to translate graphics shader body.", Severity: ShaderDiagnosticSeverity.Error));
                }
            }
        }

        var module = new ShaderIrModule
        {
            Stage = stage,
            SourceEntryPointName = entry.Name,
            EntryPointName = entry.Name,
            Resources = resources,
            Structs = structures.Values.OrderBy(structure => structure.GlslName, StringComparer.Ordinal).ToArray(),
            Requirements = [$"Vulkan {resultOptions.Profile}", $"GLSL {resultOptions.Glsl}", $"SPIRV {resultOptions.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
            Body = body,
            HelperFunctions = helperFunctions,
            Inputs = inputs,
            VertexInputs = vertexInputs,
            VertexBuffers = vertexBuffers.OrderBy(binding => binding.Binding).ToArray(),
            Outputs = outputs,
            PushConstants = pushConstants
        };
        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, resultOptions, entry.Method.Name, entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool TryBuildHelpers(
        MethodDeclarationSyntax entrySyntax,
        SemanticModel entryModel,
        ModuleCompilationContext context,
        ShaderStage stage,
        IReadOnlyDictionary<IFieldSymbol, string> pushFieldMap,
        IReadOnlyDictionary<INamedTypeSymbol, string> structNames,
        IReadOnlyDictionary<IFieldSymbol, string> structFields,
        IReadOnlyCollection<string> storageBufferTargets,
        out IReadOnlyList<string> functions,
        out IReadOnlyDictionary<IMethodSymbol, string> names,
        out string? reason)
    {
        var ordered = new List<IMethodSymbol>();
        var states = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        reason = null;
        string? failureReason = null;

        bool Visit(IMethodSymbol method)
        {
            var definition = method.OriginalDefinition;
            if (states.TryGetValue(definition, out var state))
            {
                if (state == 1)
                {
                    failureReason = $"Recursive shader helper call graph at '{definition.Name}'.";
                    return false;
                }
                return true;
            }

            if (!method.IsStatic || method.Arity != 0 || method.ReturnsVoid || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
            {
                failureReason = $"Shader helper '{definition.Name}' must be a static, non-generic value method with a non-void return type.";
                return false;
            }
            if (!TryGetHelperSyntax(definition, out var syntax) || syntax is null)
            {
                failureReason = $"Shader helper '{definition.Name}' must be declared in the shader source project.";
                return false;
            }
            if (syntax.Body is null)
            {
                failureReason = $"Shader helper '{definition.Name}' must use a block body; expression-bodied helpers are not supported yet.";
                return false;
            }
            if (!TryGetGlslType(definition.ReturnType, context, structNames, out _)
                || definition.Parameters.Any(parameter => !TryGetGlslType(parameter.Type, context, structNames, out _)))
            {
                failureReason = $"Shader helper '{definition.Name}' has an unsupported parameter or return type.";
                return false;
            }

            states[definition] = 1;
            helperNames[definition] = CreateHelperName(definition, usedNames);
            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            if (syntax.Body.DescendantNodes().OfType<ThisExpressionSyntax>().Any())
            {
                failureReason = $"Shader helper '{definition.Name}' captures managed instance state.";
                return false;
            }
            foreach (var identifier in syntax.Body.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol is IFieldSymbol field && !field.HasConstantValue && !pushFieldMap.ContainsKey(field) && !structFields.ContainsKey(field))
                {
                    failureReason = $"Shader helper '{definition.Name}' captures managed field '{field.Name}'.";
                    return false;
                }
                if (symbol is IPropertySymbol)
                {
                    failureReason = $"Shader helper '{definition.Name}' uses unsupported property state.";
                    return false;
                }
            }

            foreach (var invocation in syntax.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
                {
                    failureReason = $"Shader helper '{definition.Name}' contains an unresolved method call.";
                    return false;
                }
                if (context.Intrinsics.TryGetIntrinsic(called, out var intrinsic))
                {
                    if (!intrinsic.SupportsStage(stage))
                    {
                        failureReason = $"Intrinsic '{called.Name}' is not valid in {stage} stage.";
                        return false;
                    }
                    continue;
                }
                if (!Visit(called))
                {
                    return false;
                }
            }

            states[definition] = 2;
            ordered.Add(definition);
            return true;
        }

        if (entrySyntax.Body is not null)
        {
            foreach (var invocation in entrySyntax.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (entryModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
                {
                    continue;
                }
                if (context.Intrinsics.TryGetIntrinsic(called, out var intrinsic))
                {
                    if (!intrinsic.SupportsStage(stage))
                    {
                        reason = $"Intrinsic '{called.Name}' is not valid in {stage} stage.";
                        functions = [];
                        names = helperNames;
                        return false;
                    }
                    continue;
                }
                if (!Visit(called))
                {
                    reason = failureReason;
                    functions = [];
                    names = helperNames;
                    return false;
                }
            }
        }

        var emitted = new List<string>(ordered.Count);
        foreach (var helper in ordered)
        {
            if (!TryGetHelperSyntax(helper, out var syntax) || syntax is null || syntax.Body is null)
            {
                reason = $"Shader helper '{helper.Name}' has no translatable body.";
                functions = [];
                names = helperNames;
                return false;
            }

            var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            var signature = new List<string>(helper.Parameters.Length);
            foreach (var parameter in helper.Parameters)
            {
                if (!TryGetGlslType(parameter.Type, context, structNames, out var glslType))
                {
                    reason = $"Shader helper '{helper.Name}' has an unsupported parameter type.";
                    functions = [];
                    names = helperNames;
                    return false;
                }
                var parameterName = "arg_" + Sanitize(parameter.Name);
                parameterMap[parameter] = parameterName;
                signature.Add(glslType + " " + parameterName);
            }
            if (!TryGetGlslType(helper.ReturnType, context, structNames, out var returnType))
            {
                reason = $"Shader helper '{helper.Name}' has an unsupported return type.";
                functions = [];
                names = helperNames;
                return false;
            }
            if (!GraphicsShaderBodyTranslator.TryTranslate(syntax.Body, model, context, stage, parameterMap, pushFieldMap, structNames, structFields, storageBufferTargets, helperNames, out var body, out var bodyReason))
            {
                reason = bodyReason ?? $"Unable to translate shader helper '{helper.Name}'.";
                functions = [];
                names = helperNames;
                return false;
            }
            emitted.Add(returnType + " " + helperNames[helper] + "(" + string.Join(", ", signature) + ") " + body);
        }

        functions = emitted;
        names = helperNames;
        return true;
    }

    private static bool TryGetHelperSyntax(IMethodSymbol method, out MethodDeclarationSyntax? syntax)
    {
        syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        return syntax is not null;
    }

    private static bool TryGetGlslType(ITypeSymbol type, ModuleCompilationContext context, IReadOnlyDictionary<INamedTypeSymbol, string> structNames, out string glslType)
    {
        if (type is INamedTypeSymbol namedType && structNames.TryGetValue(namedType, out glslType))
        {
            return true;
        }
        return TryMapType(type, context, out glslType);
    }

    private static string CreateHelperName(IMethodSymbol method, ISet<string> usedNames)
    {
        var baseName = "delta_helper_" + Sanitize(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = baseName + "_" + suffix++;
        }
        return candidate;
    }

    private static void AddDiagnostic(List<ShaderDiagnostic> diagnostics, string id, string message, FileLinePositionSpan? location)
        => diagnostics.Add(new ShaderDiagnostic(id, message, location?.Path, location is null ? 0 : location.Value.StartLinePosition.Line + 1, location is null ? 0 : location.Value.StartLinePosition.Character + 1));

    private static bool Same(ITypeSymbol? left, ITypeSymbol? right)
        => left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);

    private static uint GetUIntArg(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is not null
            ? Convert.ToUInt32(attribute.ConstructorArguments[index].Value)
            : 0;

    private static uint GetUIntNamedArg(AttributeData attribute, string name)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == name && namedArgument.Value.Value is not null)
            {
                return Convert.ToUInt32(namedArgument.Value.Value);
            }
        }

        return 0;
    }

    private static VertexInputRate GetInputRate(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "InputRate" && namedArgument.Value.Value is int value)
            {
                return (VertexInputRate)value;
            }
        }

        return VertexInputRate.Vertex;
    }

    private static bool SupportsStage(AttributeData attribute, ShaderStage stage)
    {
        if (attribute.ConstructorArguments.Length < 3 || attribute.ConstructorArguments[2].Value is null)
        {
            return stage is ShaderStage.Vertex or ShaderStage.Fragment;
        }

        var mask = Convert.ToInt32(attribute.ConstructorArguments[2].Value);
        var required = stage switch
        {
            ShaderStage.Compute => (int)ShaderStageMask.Compute,
            ShaderStage.Vertex => (int)ShaderStageMask.Vertex,
            ShaderStage.Fragment => (int)ShaderStageMask.Fragment,
            _ => 0
        };
        return (mask & required) != 0;
    }

    private static bool TryMapType(ITypeSymbol type, ModuleCompilationContext context, out string glslType)
    {
        if (context.Intrinsics.TryMapType(type, out glslType))
        {
            return true;
        }
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

    private static bool TryGetVertexInputLayout(string glslType, out uint byteSize, out uint alignment, out string formatHint)
    {
        (byteSize, alignment, formatHint) = glslType switch
        {
            "float" => (4u, 4u, "VK_FORMAT_R32_SFLOAT"),
            "int" => (4u, 4u, "VK_FORMAT_R32_SINT"),
            "uint" => (4u, 4u, "VK_FORMAT_R32_UINT"),
            "vec2" => (8u, 4u, "VK_FORMAT_R32G32_SFLOAT"),
            "ivec2" => (8u, 4u, "VK_FORMAT_R32G32_SINT"),
            "uvec2" => (8u, 4u, "VK_FORMAT_R32G32_UINT"),
            "vec3" => (12u, 4u, "VK_FORMAT_R32G32B32_SFLOAT"),
            "ivec3" => (12u, 4u, "VK_FORMAT_R32G32B32_SINT"),
            "uvec3" => (12u, 4u, "VK_FORMAT_R32G32B32_UINT"),
            "vec4" => (16u, 4u, "VK_FORMAT_R32G32B32A32_SFLOAT"),
            "ivec4" => (16u, 4u, "VK_FORMAT_R32G32B32A32_SINT"),
            "uvec4" => (16u, 4u, "VK_FORMAT_R32G32B32A32_UINT"),
            _ => default
        };

        return byteSize != 0;
    }

    private static bool TryBuildStruct(INamedTypeSymbol type, ModuleCompilationContext context, Dictionary<INamedTypeSymbol, ShaderIrStruct> definitions, HashSet<INamedTypeSymbol> visiting, out ShaderIrStruct? structure, out string? reason)
    {
        if (definitions.TryGetValue(type, out var existing)) { structure = existing; reason = null; return true; }
        if (!visiting.Add(type)) { structure = null; reason = $"Recursive shader struct '{type.ToDisplayString()}' is not supported."; return false; }
        var layout = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layout?.ConstructorArguments.FirstOrDefault().Value is int kind && (kind == 2 || kind == 3))
        { visiting.Remove(type); structure = null; reason = $"Shader struct '{type.ToDisplayString()}' uses explicit or auto layout."; return false; }
        var members = new List<ShaderIrStructMember>();
        uint offset = 0, alignment = 1;
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            if (!TryMapType(field.Type, context, out var glslType) && field.Type is INamedTypeSymbol nested && nested.TypeKind == TypeKind.Struct)
            {
                if (!TryBuildStruct(nested, context, definitions, visiting, out var nestedStruct, out reason) || nestedStruct is null) { structure = null; visiting.Remove(type); return false; }
                glslType = nestedStruct.GlslName;
                var nestedLayout = ShaderStd430Layout.ForStruct(nestedStruct.Alignment, nestedStruct.Size);
                offset = AlignUp(offset, nestedLayout.Alignment);
                members.Add(new ShaderIrStructMember { Name = field.Name, GlslName = "member_" + Sanitize(field.Name), GlslType = glslType, Offset = offset, Alignment = nestedLayout.Alignment, Size = nestedLayout.Size, ArrayStride = nestedLayout.ArrayStride, Members = nestedStruct.Members });
                offset += nestedLayout.Size; alignment = Math.Max(alignment, nestedLayout.Alignment); continue;
            }
            if (string.IsNullOrEmpty(glslType)) { structure = null; visiting.Remove(type); reason = $"Shader struct field '{field.Name}' has unsupported type '{field.Type}'."; return false; }
            var fieldLayout = ShaderStd430Layout.ForGlslType(glslType);
            offset = AlignUp(offset, fieldLayout.Alignment);
            members.Add(new ShaderIrStructMember { Name = field.Name, GlslName = "member_" + Sanitize(field.Name), GlslType = glslType, Offset = offset, Alignment = fieldLayout.Alignment, Size = fieldLayout.Size, ArrayStride = fieldLayout.ArrayStride, MatrixStride = fieldLayout.MatrixStride });
            offset += fieldLayout.Size; alignment = Math.Max(alignment, fieldLayout.Alignment);
        }
        if (members.Count == 0) { structure = null; visiting.Remove(type); reason = $"Shader struct '{type.ToDisplayString()}' has no instance data fields."; return false; }
        structure = new ShaderIrStruct { Name = type.ToDisplayString(), GlslName = "DeltaStruct_" + Sanitize(type.ToDisplayString()), Alignment = alignment, Size = AlignUp(offset, alignment), ArrayStride = AlignUp(offset, alignment), Members = members };
        definitions[type] = structure; visiting.Remove(type); reason = null; return true;
    }

    private static uint AlignUp(uint value, uint alignment) => alignment == 0 ? value : (value + alignment - 1) / alignment * alignment;
    private static string Sanitize(string value) => new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
}

internal static class GraphicsShaderBodyTranslator
{
    public static bool TryTranslate(SyntaxNode body, SemanticModel model, ModuleCompilationContext context, ShaderStage stage, IReadOnlyDictionary<IParameterSymbol, string> parameterMap, IReadOnlyDictionary<IFieldSymbol, string> pushFieldMap, IReadOnlyDictionary<INamedTypeSymbol, string> structNames, IReadOnlyDictionary<IFieldSymbol, string> structFields, IReadOnlyCollection<string> storageBufferTargets, IReadOnlyDictionary<IMethodSymbol, string> helperNames, out string translated, out string? reason)
    {
        var rewriter = new Rewriter(model, context, stage, parameterMap, pushFieldMap, structNames, structFields, helperNames);
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

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly ModuleCompilationContext _context;
        private readonly ShaderStage _stage;
        private readonly IReadOnlyDictionary<IParameterSymbol, string> _parameters;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _pushFields;
        private readonly IReadOnlyDictionary<INamedTypeSymbol, string> _structNames;
        private readonly IReadOnlyDictionary<IFieldSymbol, string> _structFields;
        private readonly IReadOnlyDictionary<IMethodSymbol, string> _helperNames;
        public string? Reason { get; private set; }

        public Rewriter(SemanticModel model, ModuleCompilationContext context, ShaderStage stage, IReadOnlyDictionary<IParameterSymbol, string> parameters, IReadOnlyDictionary<IFieldSymbol, string> pushFields, IReadOnlyDictionary<INamedTypeSymbol, string> structNames, IReadOnlyDictionary<IFieldSymbol, string> structFields, IReadOnlyDictionary<IMethodSymbol, string> helperNames)
        { _model = model; _context = context; _stage = stage; _parameters = parameters; _pushFields = pushFields; _structNames = structNames; _structFields = structFields; _helperNames = helperNames; }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is IParameterSymbol parameter && _parameters.TryGetValue(parameter, out var parameterName))
            {
                return SyntaxFactory.ParseName(parameterName);
            }
            return base.VisitIdentifierName(node);
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
            if (symbol is IFieldSymbol field && _pushFields.TryGetValue(field, out var fieldName))
            {
                return SyntaxFactory.ParseExpression(fieldName);
            }
            if (symbol is IFieldSymbol structField && _structFields.TryGetValue(structField, out var structFieldName))
            {
                var receiver = Visit(node.Expression)?.ToFullString() ?? node.Expression.ToFullString();
                return SyntaxFactory.ParseExpression(receiver + "." + structFieldName);
            }
            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var args = node.ArgumentList.Arguments.Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node.")).ToArray();
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
                return SyntaxFactory.ParseExpression(binding.GlslName + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            if (symbol is not null && _helperNames.TryGetValue(symbol.OriginalDefinition, out var helperName))
            {
                return SyntaxFactory.ParseExpression(helperName + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var type = _model.GetTypeInfo(node).Type;
            if (type is not null && _context.Intrinsics.TryMapType(type, out var glslType))
            {
                var args = node.ArgumentList?.Arguments.Select(argument => Visit(argument.Expression) ?? throw new InvalidOperationException("Shader expression visitor returned no argument node.")).ToArray() ?? Array.Empty<ExpressionSyntax>();
                return SyntaxFactory.ParseExpression(glslType + "(" + string.Join(", ", args.Select(argument => argument.ToFullString())) + ")");
            }
            return base.VisitObjectCreationExpression(node);
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
    }
}
