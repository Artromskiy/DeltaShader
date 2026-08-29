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
                (entryPointIdentity is null || ShaderMethodIdentity.Get(entry.Method) == entryPointIdentity))
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
        if (!IsContextGraphicsEntryPoint(entry, context))
        {
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH002,
                "Graphics shader entry point must use one static in-context parameter with a [Interstage] payload.",
                Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(entry.Name, false, diagnostics,
                sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        return ValidateAndBuildContextEntryPoint(context, entry, resultOptions);
    }

    private static bool IsContextGraphicsEntryPoint(ShaderEntryPointSymbol entry, ModuleCompilationContext context)
    {
        if (entry.Method.Parameters.Length != 1 ||
            entry.Method.Parameters[0].RefKind != RefKind.In ||
            entry.Method.Parameters[0].Type is not INamedTypeSymbol contextType)
        {
            return false;
        }

        return contextType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic)
            .Any(field => field.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)));
    }

    private static ShaderCompilationResult ValidateAndBuildContextEntryPoint(
        ModuleCompilationContext context,
        ShaderEntryPointSymbol entry,
        ShaderCompilationOptions options)
    {
        var diagnostics = new List<ShaderDiagnostic>();
        var parameter = entry.Method.Parameters[0];
        var location = parameter.Locations.FirstOrDefault()?.GetLineSpan();
        if (!entry.Method.IsStatic || parameter.RefKind != RefKind.In || parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } contextType)
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                "A graphics shader context must be a single static 'in' struct parameter.", location);
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        var varyingFields = contextType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && field.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)))
            .ToArray();
        if (varyingFields.Length != 1)
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "A graphics context must contain exactly one [Interstage] payload field.", location);
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        if (varyingFields[0].Type is not INamedTypeSymbol varyingType || varyingType.TypeKind != TypeKind.Struct ||
            !varyingType.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)))
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "The [Interstage] context field must contain a struct marked [Interstage].", varyingFields[0].Locations.FirstOrDefault()?.GetLineSpan());
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        var varyingMembers = varyingType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic)
            .ToArray();
        var positionMembers = varyingMembers
            .Where(field => field.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.PositionAttributeType)))
            .ToArray();
        if (positionMembers.Length != 1 || !TryMapType(positionMembers[0].Type, context, out var positionType) || positionType != "vec4")
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "A [Interstage] payload must contain exactly one [Position] float4 field.", varyingType.Locations.FirstOrDefault()?.GetLineSpan());
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
        var directFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var pushFieldMap = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var outputFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);

        if (entry.Stage == ShaderStage.Vertex)
        {
            var offset = 0u;
            foreach (var field in varyingMembers)
            {
                var fieldLocation = field.GetAttributes().FirstOrDefault(attribute =>
                    Same(attribute.AttributeClass, context.LayoutAttributeType) && attribute.ConstructorArguments.Length == 1);
                if (!TryMapType(field.Type, context, out var glslType))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex payload field '{field.Name}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                if (fieldLocation is null)
                {
                    if (positionMembers.Any(position => SymbolEqualityComparer.Default.Equals(position, field)))
                    {
                        directFields[field] = "gl_Position";
                        outputFields[field] = "gl_Position";
                        outputs.Add(new ShaderIrInterfaceVariable
                        {
                            Name = field.Name,
                            ParameterName = field.Name,
                            GlslType = "vec4",
                            GlslName = "gl_Position",
                            Builtin = "Position"
                        });
                    }
                    else
                    {
                        var outputName = Sanitize(field.Name);
                        directFields[field] = outputName;
                        outputFields[field] = outputName;
                        outputs.Add(new ShaderIrInterfaceVariable
                        {
                            Name = field.Name,
                            ParameterName = field.Name,
                            GlslType = glslType,
                            GlslName = outputName,
                            Location = (uint)outputs.Count(output => output.Builtin is null)
                        });
                    }

                    continue;
                }

                if (!TryGetVertexInputLayout(glslType, out var byteSize, out var alignment, out var formatHint))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex payload field '{field.Name}' has an unsupported vertex input type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                var fieldName = "vertex_" + Sanitize(field.Name);
                var fieldLocationValue = GetUIntArg(fieldLocation, 0);
                if (vertexInputs.Any(input => input.Location == fieldLocationValue))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex input location {fieldLocationValue} is declared more than once.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                directFields[field] = fieldName;
                vertexInputs.Add(new ShaderIrVertexInput
                {
                    Name = fieldName,
                    ParameterName = field.Name,
                    GlslName = fieldName,
                    GlslType = glslType,
                    Location = fieldLocationValue,
                    Binding = 0,
                    ByteOffset = offset,
                    InputRate = VertexInputRate.Vertex,
                    ByteSize = byteSize,
                    Alignment = alignment,
                    FormatHint = formatHint
                });
                offset += byteSize;
                if (positionMembers.Any(position => SymbolEqualityComparer.Default.Equals(position, field)))
                {
                    outputFields[field] = "gl_Position";
                    outputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = field.Name,
                        ParameterName = field.Name,
                        GlslType = "vec4",
                        GlslName = "gl_Position",
                        Builtin = "Position"
                    });
                }
                else
                {
                    var outputName = Sanitize(field.Name);
                    outputFields[field] = outputName;
                    outputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = field.Name,
                        ParameterName = field.Name,
                        GlslType = glslType,
                        GlslName = outputName,
                        Location = (uint)outputs.Count(output => output.Builtin is null)
                    });
                }
            }

            if (vertexInputs.Count > 0)
            {
                vertexBuffers.Add(new ShaderIrVertexBufferBinding
                {
                    Binding = 0,
                    Stride = AlignUp(offset, 4),
                    InputRate = VertexInputRate.Vertex,
                    Attributes = vertexInputs
                });
            }
        }
        else
        {
            var varyingLocation = 0u;
            foreach (var field in varyingMembers)
            {
                if (positionMembers.Any(position => SymbolEqualityComparer.Default.Equals(position, field)))
                {
                    directFields[field] = "gl_FragCoord";
                    inputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = field.Name,
                        ParameterName = field.Name,
                        GlslType = "vec4",
                        GlslName = "gl_FragCoord",
                        Builtin = "FragmentPosition"
                    });
                    continue;
                }

                if (!TryMapType(field.Type, context, out var glslType))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                        $"Interstage field '{field.Name}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                var varyingName = Sanitize(field.Name);
                directFields[field] = varyingName;
                inputs.Add(new ShaderIrInterfaceVariable
                {
                    Name = field.Name,
                    ParameterName = field.Name,
                    GlslType = glslType,
                    GlslName = varyingName,
                    Location = varyingLocation++
                });
            }

            outputs.Add(new ShaderIrInterfaceVariable
            {
                Name = "FragmentColor",
                ParameterName = "return",
                GlslType = "vec4",
                GlslName = "fragColor",
                Location = 0,
                Builtin = "FragmentColor"
            });
        }

        foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            var attributes = field.GetAttributes();
            if (attributes.Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)))
            {
                continue;
            }

            var layout = attributes.FirstOrDefault(attribute => Same(attribute.AttributeClass, context.LayoutAttributeType));
            if (layout is not null)
            {
                if (layout.ConstructorArguments.Length != 2)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                        $"Context resource '{field.Name}' requires [Layout(set, binding)].", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                var set = GetUIntArg(layout, 0);
                var binding = GetUIntArg(layout, 1);
                if (!seenBindings.Add((set, binding)))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH005,
                        $"Graphics resources cannot share set {set}, binding {binding}.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                var fieldType = field.Type as INamedTypeSymbol;
                var readOnly = context.ReadOnlyStorageBufferType is not null && fieldType is not null &&
                    SymbolEqualityComparer.Default.Equals(fieldType.OriginalDefinition, context.ReadOnlyStorageBufferType);
                var readWrite = context.ReadWriteStorageBufferType is not null && fieldType is not null &&
                    SymbolEqualityComparer.Default.Equals(fieldType.OriginalDefinition, context.ReadWriteStorageBufferType);
                if (context.SampledTexture2DType is not null && SymbolEqualityComparer.Default.Equals(field.Type, context.SampledTexture2DType))
                {
                    resources.Add(new ShaderIrResource
                    {
                        Name = field.Name,
                        ParameterName = field.Name,
                        Category = ShaderResourceKind.SampledTexture2D,
                        Stage = entry.Stage,
                        Set = set,
                        Binding = binding,
                        GlslType = "sampler2D",
                        ReadOnly = true,
                        Access = ShaderResourceAccess.ReadOnly,
                        Layout = "opaque"
                    });
                    directFields[field] = field.Name;
                    continue;
                }

                if ((readOnly || readWrite) && fieldType is not null && fieldType.TypeArguments.Length == 1)
                {
                    var elementType = fieldType.TypeArguments[0];
                    string? glslType = null;
                    ShaderIrStruct? elementStruct = null;
                    if (!TryMapType(elementType, context, out glslType) && elementType is INamedTypeSymbol namedElement &&
                        TryBuildStruct(namedElement, context, structures, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out elementStruct, out _))
                    {
                        glslType = elementStruct?.GlslName;
                    }

                    if (glslType is not null)
                    {
                        resources.Add(new ShaderIrResource
                        {
                            Name = field.Name,
                            ParameterName = field.Name,
                            Category = ShaderResourceKind.StorageBuffer,
                            Stage = entry.Stage,
                            Set = set,
                            Binding = binding,
                            GlslType = glslType,
                            ReadOnly = readOnly,
                            Access = readOnly ? ShaderResourceAccess.ReadOnly : ShaderResourceAccess.ReadWrite,
                            Layout = ShaderStd430Layout.Standard,
                            Std430Layout = elementStruct is null ? ShaderStd430Layout.ForGlslType(glslType) : ShaderStd430Layout.ForStruct(elementStruct.Alignment, elementStruct.Size),
                            Members = elementStruct?.Members ?? []
                        });
                        storageBufferTargets.Add(field.Name);
                        directFields[field] = field.Name;
                        continue;
                    }
                }

                AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006,
                    $"Context resource '{field.Name}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                continue;
            }

            var push = attributes.FirstOrDefault(attribute => Same(attribute.AttributeClass, context.PushConstantAttributeType));
            if (push is not null)
            {
                if (field.Type is not INamedTypeSymbol pushType)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006,
                        $"Push constant field '{field.Name}' must be a sequential shader struct.", field.Locations.FirstOrDefault()?.GetLineSpan());
                }
                else if (!TryBuildStruct(pushType, context, structures, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out var pushStruct, out var pushReason) || pushStruct is null)
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH006,
                        pushReason ?? $"Push constant field '{field.Name}' must be a sequential shader struct.", field.Locations.FirstOrDefault()?.GetLineSpan());
                }
                else
                {
                    pushConstants.Add(new ShaderIrPushConstant
                    {
                        Name = "DeltaPushConstants",
                        ParameterName = field.Name,
                        GlslType = pushStruct.GlslName,
                        Alignment = pushStruct.Alignment,
                        Size = pushStruct.Size,
                        ArrayStride = pushStruct.ArrayStride,
                        Members = pushStruct.Members
                    });
                    foreach (var pushField in pushType.GetMembers().OfType<IFieldSymbol>().Where(pushField => !pushField.IsStatic))
                    {
                        var member = pushStruct.Members.FirstOrDefault(candidate => candidate.Name == pushField.Name);
                        if (member is not null)
                        {
                            pushFieldMap[pushField] = "pushConstants." + member.GlslName;
                        }
                    }
                    structures.Remove(pushType);
                }
                continue;
            }

            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                $"Graphics context field '{field.Name}' must use [Interstage], [Layout(set, binding)], or [PushConstant].", field.Locations.FirstOrDefault()?.GetLineSpan());
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
            if (syntax is null || (syntax.Body is null && syntax.ExpressionBody?.Expression is null))
            {
                diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, "Graphics shader entry point body is required.", Severity: ShaderDiagnosticSeverity.Error));
            }
            else
            {
                var executableBody = GetExecutableBody(syntax);
                var semanticModel = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
                if (!TryBuildHelpers(syntax, semanticModel, context, entry.Stage, pushFieldMap, structNames, structFields, storageBufferTargets, out helperFunctions, out helperNames, out var helperReason))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, helperReason ?? "Unable to lower shader helper call graph.", Severity: ShaderDiagnosticSeverity.Error));
                }
                else if (!ShaderBodyTranslator.TryTranslate(
                    executableBody,
                    semanticModel,
                    context,
                    entry.Stage,
                    parameterMap,
                    pushFieldMap,
                    structNames,
                    structFields,
                    storageBufferTargets,
                    helperNames,
                    out body,
                    out var reason,
                    directFields,
                    outputFields,
                    entry.Stage == ShaderStage.Vertex ? varyingType : null,
                    lowerReturns: true))
                {
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticId.DSH008, reason ?? "Unable to translate graphics shader body.", Severity: ShaderDiagnosticSeverity.Error));
                }
                else if (entry.Stage == ShaderStage.Vertex)
                {
                    AddVertexBuiltinInputs(executableBody, semanticModel, context, inputs);
                }
            }
        }

        var module = new ShaderIrModule
        {
            Stage = entry.Stage,
            SourceEntryPointName = entry.Name,
            EntryPointName = entry.Name,
            Resources = resources,
            Structs = structures.Values.OrderBy(structure => structure.GlslName, StringComparer.Ordinal).ToArray(),
            Requirements = [$"Vulkan {options.Profile}", $"GLSL {options.Glsl}", $"SPIRV {options.Spirv}"],
            Instructions = new[] { "entrypoint " + entry.Name },
            Body = body,
            HelperFunctions = context.Intrinsics.GetGlslHelperFunctions(
                    entry.Stage,
                    new[] { body }.Concat(helperFunctions))
                .Concat(helperFunctions)
                .ToArray(),
            Inputs = inputs,
            VertexInputs = vertexInputs,
            VertexBuffers = vertexBuffers,
            Outputs = outputs,
            PushConstants = pushConstants
        };
        return new ShaderCompilationResult(entry.Name, diagnostics.Count == 0, diagnostics, module, options, entry.Method.Name, ShaderMethodIdentity.Get(entry.Method));
    }

    private static SyntaxNode GetExecutableBody(MethodDeclarationSyntax syntax)
    {
        if (syntax.Body is { } body)
        {
            return body;
        }

        if (syntax.ExpressionBody is { Expression: { } expression })
        {
            return expression;
        }

        throw new InvalidOperationException("A graphics entry point must have a block or expression body.");
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
            SyntaxNode? helperBody = syntax.Body ?? (SyntaxNode?)syntax.ExpressionBody?.Expression;
            if (helperBody is null)
            {
                failureReason = $"Shader helper '{definition.Name}' must have a translatable body.";
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
            if (!TryGetSemanticModel(context.Compilation, syntax.SyntaxTree, out var model))
            {
                failureReason = $"Shader helper '{definition.Name}' is not declared in the active shader compilation.";
                return false;
            }
            if (helperBody.DescendantNodesAndSelf().OfType<ThisExpressionSyntax>().Any())
            {
                failureReason = $"Shader helper '{definition.Name}' captures managed instance state.";
                return false;
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

            foreach (var invocation in helperBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
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

        var entryBody = entrySyntax.Body ?? (SyntaxNode?)entrySyntax.ExpressionBody?.Expression;
        if (entryBody is not null)
        {
            foreach (var invocation in entryBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
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
            if (!TryGetHelperSyntax(helper, out var syntax) || syntax is null)
            {
                reason = $"Shader helper '{helper.Name}' has no translatable body.";
                functions = [];
                names = helperNames;
                return false;
            }

            SyntaxNode? helperBody = syntax.Body ?? (SyntaxNode?)syntax.ExpressionBody?.Expression;
            if (helperBody is null)
            {
                reason = $"Shader helper '{helper.Name}' has no translatable body.";
                functions = [];
                names = helperNames;
                return false;
            }

            if (!TryGetSemanticModel(context.Compilation, syntax.SyntaxTree, out var model))
            {
                reason = $"Shader helper '{helper.Name}' is not declared in the active shader compilation.";
                functions = [];
                names = helperNames;
                return false;
            }
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
            if (!ShaderBodyTranslator.TryTranslate(helperBody, model, context, stage, parameterMap, pushFieldMap, structNames, structFields, storageBufferTargets, helperNames, out var body, out var bodyReason))
            {
                reason = bodyReason ?? $"Unable to translate shader helper '{helper.Name}'.";
                functions = [];
                names = helperNames;
                return false;
            }
            var functionBody = syntax.Body is null ? "{ return " + body + "; }" : body;
            emitted.Add(returnType + " " + helperNames[helper] + "(" + string.Join(", ", signature) + ") " + functionBody);
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

    private static bool TryGetSemanticModel(
        Compilation compilation,
        SyntaxTree syntaxTree,
        [NotNullWhen(true)] out SemanticModel? model)
    {
        if (!compilation.SyntaxTrees.Contains(syntaxTree))
        {
            model = null;
            return false;
        }

        model = compilation.GetSemanticModel(syntaxTree);
        return true;
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

    private static void AddVertexBuiltinInputs(
        SyntaxNode body,
        SemanticModel model,
        ModuleCompilationContext context,
        List<ShaderIrInterfaceVariable> inputs)
    {
        foreach (var memberAccess in body.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (model.GetSymbolInfo(memberAccess).Symbol is not IPropertySymbol property ||
                !context.Intrinsics.TryGetIntrinsic(property, out var binding) ||
                binding.Category != IntrinsicCategory.Builtin ||
                !binding.SupportsStage(ShaderStage.Vertex) ||
                inputs.Any(input => string.Equals(input.Builtin, property.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            var glslType = property.Name switch
            {
                "VertexIndex" or "InstanceIndex" => "uint",
                _ => string.Empty
            };
            if (glslType.Length == 0)
            {
                continue;
            }

            var name = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            inputs.Add(new ShaderIrInterfaceVariable
            {
                Name = name,
                ParameterName = name,
                GlslName = binding.GlslName,
                GlslType = glslType,
                Builtin = property.Name
            });
        }
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
