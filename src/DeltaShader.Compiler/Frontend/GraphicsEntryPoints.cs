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
                "Graphics shader entry point must use static 'in' context and payload parameters.",
                Severity: ShaderDiagnosticSeverity.Error));
            return new ShaderCompilationResult(entry.Name, false, diagnostics,
                sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        return ValidateAndBuildContextEntryPoint(context, entry, resultOptions);
    }

    private static bool IsContextGraphicsEntryPoint(ShaderEntryPointSymbol entry, ModuleCompilationContext context)
    {
        if (entry.Method.Parameters.Length != 2 ||
            entry.Method.Parameters[0].RefKind != RefKind.In ||
            entry.Method.Parameters[0].Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } ||
            entry.Method.Parameters[1].RefKind != RefKind.In ||
            entry.Method.Parameters[1].Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } payloadType ||
            !IsInterstagePayload(payloadType, context))
        {
            return false;
        }

        return true;
    }

    private static ShaderCompilationResult ValidateAndBuildContextEntryPoint(
        ModuleCompilationContext context,
        ShaderEntryPointSymbol entry,
        ShaderCompilationOptions options)
    {
        var diagnostics = new List<ShaderDiagnostic>();
        var contextParameter = entry.Method.Parameters[0];
        var payloadParameter = entry.Method.Parameters[1];
        var location = contextParameter.Locations.FirstOrDefault()?.GetLineSpan();
        if (!entry.Method.IsStatic ||
            contextParameter.RefKind != RefKind.In ||
            contextParameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } contextType ||
            payloadParameter.RefKind != RefKind.In ||
            payloadParameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } inputType)
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH002,
                "A graphics shader entry point must use static 'in' context and payload struct parameters.", location);
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        var contextInterstageFields = contextType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && IsInterstageField(field, context))
            .ToArray();
        if (contextInterstageFields.Length != 0)
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "A graphics context must not contain an interstage payload field; pass the payload as the second entry-point parameter.",
                contextInterstageFields[0].Locations.FirstOrDefault()?.GetLineSpan() ?? location);
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        if (!IsInterstagePayload(inputType, context))
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "The second graphics entry-point parameter must be a semantic interstage struct.",
                payloadParameter.Locations.FirstOrDefault()?.GetLineSpan());
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        var outputType = entry.Stage == ShaderStage.Vertex
            ? entry.Method.ReturnType as INamedTypeSymbol
            : null;
        if (entry.Stage == ShaderStage.Vertex &&
            (outputType is null || outputType.TypeKind != TypeKind.Struct || !IsInterstagePayload(outputType, context)))
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "A vertex shader must return a semantic interstage struct.",
                entry.Method.ReturnType.Locations.FirstOrDefault()?.GetLineSpan() ?? location);
            return new ShaderCompilationResult(entry.Name, false, diagnostics, sourceMethodName: entry.Method.Name,
                sourceMethodIdentity: ShaderMethodIdentity.Get(entry.Method));
        }

        var inputLeaves = ShaderInterstageTraversal.Flatten(inputType, context,
            (field, message) => AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013, message,
                field.Locations.FirstOrDefault()?.GetLineSpan()));
        var outputLeaves = outputType is null
            ? inputLeaves
            : ShaderInterstageTraversal.Flatten(outputType, context,
                (field, message) => AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013, message,
                    field.Locations.FirstOrDefault()?.GetLineSpan()));
        var varyingLeaves = entry.Stage == ShaderStage.Vertex ? outputLeaves : inputLeaves;
        var seenLeafSymbols = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        foreach (var leaf in varyingLeaves)
        {
            if (!seenLeafSymbols.Add(leaf.Field))
            {
                AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                    $"Interstage semantic field '{leaf.PathName}' is present more than once through nested payloads.",
                    leaf.Field.Locations.FirstOrDefault()?.GetLineSpan());
            }
        }

        var positionLeaves = entry.Stage == ShaderStage.Vertex
            ? varyingLeaves.Where(leaf => IsPositionMember(leaf.Field, context)).ToArray()
            : Array.Empty<ShaderInterstageLeaf>();
        if (entry.Stage == ShaderStage.Vertex &&
            (positionLeaves.Length != 1 || !TryMapType(positionLeaves[0].Field.Type, context, out var positionType) || positionType != "vec4"))
        {
            AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                "A semantic interstage payload must contain exactly one Delta.Shader.Position field.",
                (outputType ?? inputType).Locations.FirstOrDefault()?.GetLineSpan());
        }

        var positionFields = new HashSet<IFieldSymbol>(
            varyingLeaves.Where(leaf => IsPositionMember(leaf.Field, context)).Select(leaf => leaf.Field),
            SymbolEqualityComparer.Default);

        var inputs = new List<ShaderIrInterfaceVariable>();
        var vertexInputs = new List<ShaderIrVertexInput>();
        var vertexBuffers = new List<ShaderIrVertexBufferBinding>();
        var outputs = new List<ShaderIrInterfaceVariable>();
        var pushConstants = new List<ShaderIrPushConstant>();
        var resources = new List<ShaderIrResource>();
        var contextFields = new List<ShaderIrContextField>();
        var storageBufferTargets = new HashSet<string>(StringComparer.Ordinal);
        var seenBindings = new HashSet<(uint Set, uint Binding)>();
        var structures = new Dictionary<INamedTypeSymbol, ShaderIrStruct>(SymbolEqualityComparer.Default);
        var directFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var pushFieldMap = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var outputFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var parameterMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);

        if (entry.Stage == ShaderStage.Vertex)
        {
            var vertexInputCandidates = new List<VertexInputCandidate>();
            foreach (var leaf in inputLeaves)
            {
                var field = leaf.Field;
                var fieldLabel = leaf.PathName;
                var fieldLocation = field.GetAttributes().FirstOrDefault(attribute =>
                    Same(attribute.AttributeClass, context.LayoutAttributeType) && attribute.ConstructorArguments.Length == 1);
                if (!TryMapType(field.Type, context, out var glslType))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex payload field '{fieldLabel}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                if (fieldLocation is null)
                {
                    continue;
                }

                if (!TryGetVertexInputLayout(glslType, out var byteSize, out var alignment, out var formatHint))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex payload field '{fieldLabel}' has an unsupported vertex input type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                if (!TryGetVertexInputShape(glslType, out var scalarType, out var componentCount))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex payload field '{fieldLabel}' has an unsupported vertex input shape.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                vertexInputCandidates.Add(new VertexInputCandidate
                {
                    Field = field,
                    FieldLabel = fieldLabel,
                    FieldName = "vertex_" + Sanitize(fieldLabel),
                    ParameterName = field.Name,
                    GlslType = glslType,
                    Location = GetUIntArg(fieldLocation, 0),
                    ByteSize = byteSize,
                    Alignment = alignment,
                    FormatHint = formatHint,
                    ScalarType = scalarType,
                    ComponentCount = componentCount
                });
            }

            foreach (var leaf in outputLeaves)
            {
                var field = leaf.Field;
                var fieldLabel = leaf.PathName;
                if (!TryMapType(field.Type, context, out var glslType))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                        $"Vertex output field '{fieldLabel}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                if (positionFields.Contains(field))
                {
                    outputFields[field] = "gl_Position";
                    outputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = fieldLabel,
                        ParameterName = fieldLabel,
                        GlslType = "vec4",
                        GlslName = "gl_Position",
                        Builtin = "Position"
                    });
                }
                else
                {
                    var outputName = Sanitize(fieldLabel);
                    outputFields[field] = outputName;
                    outputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = fieldLabel,
                        ParameterName = fieldLabel,
                        GlslType = glslType,
                        GlslName = outputName,
                        Location = (uint)outputs.Count(output => output.Builtin is null)
                    });
                }
            }

            var logicalVertexInputs = new List<ShaderIrVertexInput>();
            ResolveVertexInputSlots(vertexInputCandidates, vertexInputs, logicalVertexInputs, directFields, diagnostics, out var offset);
            if (vertexInputs.Count > 0)
            {
                vertexBuffers.Add(new ShaderIrVertexBufferBinding
                {
                    Binding = 0,
                    Stride = AlignUp(offset, 4),
                    InputRate = VertexInputRate.Vertex,
                    Attributes = logicalVertexInputs
                });
            }
        }
        else
        {
            var varyingLocation = 0u;
            foreach (var leaf in inputLeaves)
            {
                var field = leaf.Field;
                var fieldLabel = leaf.PathName;
                if (positionFields.Contains(field))
                {
                    directFields[field] = "gl_FragCoord";
                    inputs.Add(new ShaderIrInterfaceVariable
                    {
                        Name = fieldLabel,
                        ParameterName = fieldLabel,
                        GlslType = "vec4",
                        GlslName = "gl_FragCoord",
                        Builtin = "FragmentPosition"
                    });
                    continue;
                }

                if (!TryMapType(field.Type, context, out var glslType))
                {
                    AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH012,
                        $"Interstage field '{fieldLabel}' has an unsupported shader type.", field.Locations.FirstOrDefault()?.GetLineSpan());
                    continue;
                }

                var varyingName = Sanitize(fieldLabel);
                directFields[field] = varyingName;
                inputs.Add(new ShaderIrInterfaceVariable
                {
                    Name = fieldLabel,
                    ParameterName = fieldLabel,
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

        foreach (var leaf in inputLeaves)
        {
            if (!TryMapType(leaf.Field.Type, context, out var glslType))
            {
                continue;
            }

            var locationAttribute = entry.Stage == ShaderStage.Vertex
                ? leaf.Field.GetAttributes().FirstOrDefault(attribute =>
                    Same(attribute.AttributeClass, context.LayoutAttributeType) && attribute.ConstructorArguments.Length == 1)
                : null;
            contextFields.Add(new ShaderIrContextField
            {
                TypeIdentity = leaf.Field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SourcePath = leaf.PathName,
                ReadGlslName = directFields.TryGetValue(leaf.Field, out var readName) ? readName : string.Empty,
                WriteGlslName = outputFields.TryGetValue(leaf.Field, out var writeName) ? writeName : string.Empty,
                GlslType = glslType,
                Kind = ShaderIrContextFieldKind.Interstage,
                Stage = entry.Stage,
                HostProvided = locationAttribute is not null,
                Location = locationAttribute is null ? null : GetUIntArg(locationAttribute, 0)
            });
        }

        foreach (var field in contextType.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
        {
            var attributes = field.GetAttributes();
            if (IsInterstageField(field, context))
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
                        TypeIdentity = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
                            TypeIdentity = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
                        TypeIdentity = pushType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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

        foreach (var resource in resources)
        {
            contextFields.Add(new ShaderIrContextField
            {
                TypeIdentity = resource.TypeIdentity,
                SourcePath = resource.ParameterName,
                GlslType = resource.GlslType ?? string.Empty,
                Kind = ShaderIrContextFieldKind.Resource,
                Stage = resource.Stage,
                HostProvided = true,
                ResourceKind = resource.Category,
                Access = resource.Access,
                Set = resource.Set,
                Binding = resource.Binding
            });
        }

        foreach (var pushConstant in pushConstants)
        {
            contextFields.Add(new ShaderIrContextField
            {
                TypeIdentity = pushConstant.TypeIdentity,
                SourcePath = pushConstant.ParameterName,
                GlslType = pushConstant.GlslType,
                Kind = ShaderIrContextFieldKind.PushConstant,
                Stage = entry.Stage,
                HostProvided = true,
                Alignment = pushConstant.Alignment,
                Size = pushConstant.Size,
                ArrayStride = pushConstant.ArrayStride
            });
        }

        string body = string.Empty;
        IReadOnlyList<string> helperFunctions = [];
        IReadOnlyDictionary<IMethodSymbol, string> helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        IReadOnlyDictionary<IMethodSymbol, bool> helperReceivers = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        if (diagnostics.Count == 0)
        {
            var structNames = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
            foreach (var definition in structures)
            {
                structNames[definition.Key] = definition.Value.GlslName;
            }
            var structFields = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
            var structProperties = new Dictionary<IPropertySymbol, string>(SymbolEqualityComparer.Default);
            AddSemanticValueMembers(structFields, structProperties, context);
            foreach (var definition in structures)
            {
                foreach (var field in definition.Key.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                {
                    var member = definition.Value.Members.FirstOrDefault(candidate => candidate.Name == field.Name);
                    if (member is not null)
                    {
                        structFields[field] = member.GlslName;
                        structFields[field.OriginalDefinition] = member.GlslName;
                    }
                }

                foreach (var property in definition.Key.GetMembers().OfType<IPropertySymbol>().Where(property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null))
                {
                    var member = definition.Value.Members.FirstOrDefault(candidate => candidate.Name == property.Name);
                    if (member is not null)
                    {
                        structProperties[property] = member.GlslName;
                        structProperties[property.OriginalDefinition] = member.GlslName;
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
                if (!TryBuildHelpers(syntax, semanticModel, context, entry.Stage, structures, pushFieldMap, structNames, structFields, structProperties, storageBufferTargets, out helperFunctions, out helperNames, out helperReceivers, out var helperReason))
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
                    entry.Stage == ShaderStage.Vertex ? outputType : null,
                    lowerReturns: true,
                    structProperties: structProperties,
                    helperReceivers: helperReceivers))
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderBodyTranslator.IsCapabilityDiagnostic(reason)
                            ? ShaderDiagnosticId.DSH007
                            : ShaderDiagnosticId.DSH008,
                        reason ?? "Unable to translate graphics shader body.",
                        Severity: ShaderDiagnosticSeverity.Error));
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
            PushConstants = pushConstants,
            ContextFields = contextFields
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
        Dictionary<INamedTypeSymbol, ShaderIrStruct> structures,
        IReadOnlyDictionary<IFieldSymbol, string> pushFieldMap,
        Dictionary<INamedTypeSymbol, string> structNames,
        Dictionary<IFieldSymbol, string> structFields,
        Dictionary<IPropertySymbol, string> structProperties,
        IReadOnlyCollection<string> storageBufferTargets,
        out IReadOnlyList<string> functions,
        out IReadOnlyDictionary<IMethodSymbol, string> names,
        out IReadOnlyDictionary<IMethodSymbol, bool> receivers,
        out string? reason)
    {
        var ordered = new List<IMethodSymbol>();
        var states = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var helperNames = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        var helperReceivers = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        receivers = helperReceivers;
        reason = null;
        string? failureReason = null;

        void RefreshStructMaps()
        {
            structNames.Clear();
            structFields.Clear();
            structProperties.Clear();
            AddSemanticValueMembers(structFields, structProperties, context);
            foreach (var definition in structures)
            {
                structNames[definition.Key] = definition.Value.GlslName;
                foreach (var field in definition.Key.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic && !field.IsImplicitlyDeclared))
                {
                    var member = definition.Value.Members.FirstOrDefault(candidate => candidate.Name == field.Name);
                    if (member is not null)
                    {
                        structFields[field] = member.GlslName;
                        structFields[field.OriginalDefinition] = member.GlslName;
                    }
                }

                foreach (var property in definition.Key.GetMembers().OfType<IPropertySymbol>().Where(property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null))
                {
                    var member = definition.Value.Members.FirstOrDefault(candidate => candidate.Name == property.Name);
                    if (member is not null)
                    {
                        structProperties[property] = member.GlslName;
                        structProperties[property.OriginalDefinition] = member.GlslName;
                    }
                }
            }
        }

        bool EnsureStructType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType ||
                namedType.TypeKind != TypeKind.Struct ||
                TryGetGlslType(type, context, structNames, out _) ||
                ShaderStructSupport.IsStateless(namedType) ||
                structures.ContainsKey(namedType))
            {
                return true;
            }

            if (!TryBuildStruct(namedType, context, structures,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), out _, out failureReason))
            {
                return false;
            }

            RefreshStructMaps();
            return true;
        }

        bool EnsureLocalStructTypes(SyntaxNode body, SemanticModel model)
        {
            foreach (var declaration in body.DescendantNodesAndSelf().OfType<VariableDeclarationSyntax>())
            {
                var type = declaration.Type.IsVar
                    ? declaration.Variables.Count == 1 && declaration.Variables[0].Initializer is { } initializer
                        ? model.GetTypeInfo(initializer.Value).ConvertedType ?? model.GetTypeInfo(initializer.Value).Type
                        : null
                    : model.GetTypeInfo(declaration.Type).Type;
                if (type is not null && !EnsureStructType(type))
                {
                    return false;
                }
            }

            foreach (var creation in body.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>())
            {
                var type = model.GetTypeInfo(creation).ConvertedType ?? model.GetTypeInfo(creation).Type;
                if (type is not null && !EnsureStructType(type))
                {
                    return false;
                }
            }

            RefreshStructMaps();
            return true;
        }

        RefreshStructMaps();

        bool Visit(IMethodSymbol method)
        {
            var definition = method.OriginalDefinition;
            if (states.TryGetValue(method, out var state))
            {
                if (state == 1)
                {
                    failureReason = $"Recursive shader helper call graph at '{definition.Name}'.";
                    return false;
                }
                return true;
            }

            if (!method.IsStatic && method.ContainingType.TypeKind != TypeKind.Struct)
            {
                failureReason = $"Shader helper '{definition.Name}' must be static or an instance method on a value struct.";
                return false;
            }
            if ((method.IsGenericMethod && method.TypeArguments.Any(argument => argument is ITypeParameterSymbol)) ||
                (method.ReturnsVoid && !method.Parameters.Any(parameter => parameter.RefKind == RefKind.Out)) ||
                method.Parameters.Any(parameter => parameter.RefKind != RefKind.None && parameter.RefKind != RefKind.Out))
            {
                failureReason = $"Shader helper '{definition.Name}' must be a non-generic value method or a void method with out parameters.";
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
            var hasReceiver = !method.IsStatic && !ShaderStructSupport.IsStateless(method.ContainingType);
            if (hasReceiver && !EnsureStructType(method.ContainingType) ||
                !EnsureStructType(method.ReturnType) ||
                method.Parameters.Any(parameter => !EnsureStructType(parameter.Type)))
            {
                return false;
            }

            RefreshStructMaps();
            if ((!method.ReturnsVoid && !TryGetGlslType(method.ReturnType, context, structNames, out _))
                || method.Parameters.Any(parameter => !TryGetGlslType(parameter.Type, context, structNames, out _)) ||
                (hasReceiver && !TryGetGlslType(method.ContainingType, context, structNames, out _)))
            {
                failureReason = $"Shader helper '{definition.Name}' has an unsupported parameter or return type.";
                return false;
            }

            states[method] = 1;
            helperNames[method] = CreateHelperName(method, usedNames);
            helperReceivers[method] = hasReceiver;
            if (!TryGetSemanticModel(context.Compilation, syntax.SyntaxTree, out var model))
            {
                failureReason = $"Shader helper '{definition.Name}' is not declared in the active shader compilation.";
                return false;
            }
            if (!EnsureLocalStructTypes(helperBody, model))
            {
                return false;
            }
            if (!ShaderBodyTranslator.ValidateOutParameters(syntax, model, method, out failureReason))
            {
                return false;
            }

            if (definition.IsStatic && helperBody.DescendantNodesAndSelf().OfType<ThisExpressionSyntax>().Any())
            {
                failureReason = $"Static shader helper '{definition.Name}' cannot use an instance receiver.";
                return false;
            }
            foreach (var assignment in helperBody.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                if (model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property &&
                    (property.IsStatic || !ShaderStructSupport.IsAutoProperty(property)))
                {
                    failureReason = $"Shader helper '{definition.Name}' can mutate only instance auto-properties of value structs.";
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

                if (symbol is IFieldSymbol field && !field.HasConstantValue && !pushFieldMap.ContainsKey(field) && !structFields.ContainsKey(field))
                {
                    if (!definition.IsStatic && !field.IsStatic &&
                        SymbolEqualityComparer.Default.Equals(field.ContainingType, definition.ContainingType))
                    {
                        continue;
                    }

                    failureReason = $"Shader helper '{definition.Name}' captures managed field '{field.Name}'.";
                    return false;
                }

                if (symbol is IPropertySymbol property &&
                    !context.Intrinsics.TryGetIntrinsic(symbol, out _) &&
                    !IsSupportedShaderProperty(property, definition, structProperties))
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
            if (!EnsureLocalStructTypes(entryBody, entryModel))
            {
                reason = failureReason;
                functions = [];
                names = helperNames;
                return false;
            }

            var entryMethod = entryModel.GetDeclaredSymbol(entrySyntax) as IMethodSymbol;
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
            var signature = new List<string>(helper.Parameters.Length + (helper.IsStatic ? 0 : 1));
            string? instanceReceiver = null;
            var hasReceiver = helperReceivers.TryGetValue(helper, out var receiver) ? receiver : !helper.IsStatic;
            if (hasReceiver)
            {
                if (!TryGetGlslType(helper.ContainingType, context, structNames, out var receiverType))
                {
                    reason = $"Shader helper '{helper.Name}' has an unsupported value-struct receiver type.";
                    functions = [];
                    names = helperNames;
                    return false;
                }

                instanceReceiver = "self";
                signature.Add(receiverType + " " + instanceReceiver);
            }

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
                signature.Add((parameter.RefKind == RefKind.Out ? "out " : string.Empty) + glslType + " " + parameterName);
            }
            var returnType = helper.ReturnsVoid
                ? "void"
                : TryGetGlslType(helper.ReturnType, context, structNames, out var mappedReturnType)
                    ? mappedReturnType
                    : null;
            if (returnType is null)
            {
                reason = $"Shader helper '{helper.Name}' has an unsupported return type.";
                functions = [];
                names = helperNames;
                return false;
            }

            var helperNameMap = new Dictionary<IMethodSymbol, string>(helperNames, SymbolEqualityComparer.Default);
            var helperReceiverMap = new Dictionary<IMethodSymbol, bool>(helperReceivers, SymbolEqualityComparer.Default);
            foreach (var invocation in helperBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called ||
                    context.Intrinsics.TryGetIntrinsic(called, out _))
                {
                    continue;
                }

                var target = ShaderHelperSpecialization.ResolveTarget(invocation, called, model, helper);
                if (helperNames.TryGetValue(target, out var targetName))
                {
                    helperNameMap[called] = targetName;
                    helperNameMap[called.OriginalDefinition] = targetName;
                }

                if (helperReceivers.TryGetValue(target, out var targetHasReceiver))
                {
                    helperReceiverMap[called] = targetHasReceiver;
                    helperReceiverMap[called.OriginalDefinition] = targetHasReceiver;
                }
            }

            var outputParameters = helper.Parameters
                .Where(parameter => parameter.RefKind == RefKind.Out)
                .ToArray();
            if (!ShaderBodyTranslator.TryTranslate(helperBody, model, context, stage, parameterMap, pushFieldMap, structNames, structFields, storageBufferTargets, helperNameMap, out var body, out var bodyReason, instanceReceiver: instanceReceiver, structProperties: structProperties, helperReceivers: helperReceiverMap, outputParameters: outputParameters))
            {
                reason = bodyReason ?? $"Unable to translate shader helper '{helper.Name}'.";
                functions = [];
                names = helperNames;
                return false;
            }
            var functionBody = syntax.Body is null
                ? helper.ReturnsVoid ? "{ " + body + "; }" : "{ return " + body + "; }"
                : body;
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
        if (TryMapType(type, context, out glslType))
        {
            return true;
        }

        return type is INamedTypeSymbol namedType && structNames.TryGetValue(namedType, out glslType);
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
            (HasSimpleGetter(syntax) ||
             syntax.Initializer?.Value is not null);
    }

    private static bool IsExpressionBodiedProperty(IPropertySymbol property)
        => property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax syntax &&
           HasSimpleGetter(syntax);

    private static bool HasSimpleGetter(PropertyDeclarationSyntax syntax)
    {
        if (syntax.ExpressionBody?.Expression is not null)
        {
            return true;
        }

        var getter = syntax.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        return getter?.ExpressionBody?.Expression is not null ||
            getter?.Body?.Statements.Count == 1 &&
            getter.Body.Statements[0] is ReturnStatementSyntax { Expression: not null };
    }

    private static uint GetUIntArg(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is not null
            ? Convert.ToUInt32(attribute.ConstructorArguments[index].Value)
            : 0;

    internal static bool TryMapType(ITypeSymbol type, ModuleCompilationContext context, out string glslType)
    {
        if (ShaderSemanticTypeSupport.TryMapType(type, context, out glslType))
        {
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

    private static bool IsInterstageField(IFieldSymbol field, ModuleCompilationContext context)
        => field.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)) ||
            field.Type is INamedTypeSymbol payloadType && IsInterstagePayload(payloadType, context);

    private static bool IsInterstagePayload(INamedTypeSymbol type, ModuleCompilationContext context)
        => type.GetAttributes().Any(attribute => Same(attribute.AttributeClass, context.InterstageAttributeType)) ||
            ShaderInterstageTraversal.ContainsSemanticLeaf(type, context);

    private static bool IsPositionMember(IFieldSymbol field, ModuleCompilationContext context)
        => ShaderSemanticTypeSupport.IsPosition(field.Type, context);

    private static void AddSemanticValueMembers(
        Dictionary<IFieldSymbol, string> structFields,
        Dictionary<IPropertySymbol, string> structProperties,
        ModuleCompilationContext context)
    {
        foreach (var valueMember in context.SemanticValueMembers.Values)
        {
            if (valueMember is IFieldSymbol valueField)
            {
                structFields[valueField] = string.Empty;
                structFields[valueField.OriginalDefinition] = string.Empty;
            }
            else if (valueMember is IPropertySymbol valueProperty)
            {
                structProperties[valueProperty] = string.Empty;
                structProperties[valueProperty.OriginalDefinition] = string.Empty;
            }
        }
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

    private static bool TryGetVertexInputShape(string glslType, out string scalarType, out uint componentCount)
    {
        (scalarType, componentCount) = glslType switch
        {
            "float" => ("float", 1u),
            "vec2" => ("float", 2u),
            "vec3" => ("float", 3u),
            "vec4" => ("float", 4u),
            "int" => ("int", 1u),
            "ivec2" => ("int", 2u),
            "ivec3" => ("int", 3u),
            "ivec4" => ("int", 4u),
            "uint" => ("uint", 1u),
            "uvec2" => ("uint", 2u),
            "uvec3" => ("uint", 3u),
            "uvec4" => ("uint", 4u),
            _ => (string.Empty, 0u)
        };

        return componentCount != 0;
    }

    private static void ResolveVertexInputSlots(
        IReadOnlyList<VertexInputCandidate> candidates,
        List<ShaderIrVertexInput> physicalInputs,
        List<ShaderIrVertexInput> logicalInputs,
        Dictionary<IFieldSymbol, string> directFields,
        List<ShaderDiagnostic> diagnostics,
        out uint byteLength)
    {
        var slots = new List<VertexInputSlot>();
        var slotsByLocation = new Dictionary<uint, VertexInputSlot>();
        foreach (var candidate in candidates)
        {
            if (!slotsByLocation.TryGetValue(candidate.Location, out var slot))
            {
                slot = new VertexInputSlot
                {
                    Location = candidate.Location,
                    FieldName = candidate.FieldName,
                    ScalarType = candidate.ScalarType
                };
                slotsByLocation.Add(candidate.Location, slot);
                slots.Add(slot);
            }
            else if (!string.Equals(slot.ScalarType, candidate.ScalarType, StringComparison.Ordinal))
            {
                AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                    $"Vertex input location {candidate.Location} cannot merge '{candidate.GlslType}' with a {slot.ScalarType} input.",
                    candidate.Field.Locations.FirstOrDefault()?.GetLineSpan());
                continue;
            }

            if (slot.ComponentCount + candidate.ComponentCount > 4)
            {
                AddDiagnostic(diagnostics, ShaderDiagnosticId.DSH013,
                    $"Vertex input location {candidate.Location} exceeds its four-component slot.",
                    candidate.Field.Locations.FirstOrDefault()?.GetLineSpan());
                continue;
            }

            candidate.ComponentOffset = slot.ComponentCount;
            slot.ComponentCount += candidate.ComponentCount;
            slot.Members.Add(candidate);
        }

        byteLength = 0;
        foreach (var slot in slots)
        {
            if (slot.Members.Count == 0)
            {
                continue;
            }

            var merged = slot.Members.Count > 1;
            var physicalType = merged ? slot.ScalarType switch
            {
                "float" => "vec4",
                "int" => "ivec4",
                "uint" => "uvec4",
                _ => string.Empty
            } : slot.Members[0].GlslType;
            if (!TryGetVertexInputLayout(physicalType, out var physicalByteSize, out var physicalAlignment, out var physicalFormatHint))
            {
                continue;
            }

            slot.ByteOffset = byteLength;
            byteLength += physicalByteSize;
            physicalInputs.Add(new ShaderIrVertexInput
            {
                Name = slot.FieldName,
                ParameterName = slot.Members[0].ParameterName,
                GlslName = slot.FieldName,
                GlslType = physicalType,
                Location = slot.Location,
                Binding = 0,
                ByteOffset = slot.ByteOffset,
                InputRate = VertexInputRate.Vertex,
                ByteSize = physicalByteSize,
                Alignment = physicalAlignment,
                FormatHint = physicalFormatHint
            });

            foreach (var member in slot.Members)
            {
                member.ByteOffset = slot.ByteOffset + member.ComponentOffset * 4u;
                directFields[member.Field] = slot.FieldName + (merged ? CreateSwizzle(member.ComponentOffset, member.ComponentCount) : string.Empty);
                logicalInputs.Add(new ShaderIrVertexInput
                {
                    Name = member.FieldName,
                    ParameterName = member.ParameterName,
                    GlslName = member.FieldName,
                    GlslType = member.GlslType,
                    Location = member.Location,
                    Binding = 0,
                    ByteOffset = member.ByteOffset,
                    InputRate = VertexInputRate.Vertex,
                    ByteSize = member.ByteSize,
                    Alignment = member.Alignment,
                    FormatHint = member.FormatHint
                });
            }
        }
    }

    private static string CreateSwizzle(uint componentOffset, uint componentCount)
    {
        const string components = "xyzw";
        return "." + components.Substring((int)componentOffset, (int)componentCount);
    }

    private sealed class VertexInputCandidate
    {
        public IFieldSymbol Field { get; init; } = null!;
        public string FieldLabel { get; init; } = string.Empty;
        public string FieldName { get; init; } = string.Empty;
        public string ParameterName { get; init; } = string.Empty;
        public string GlslType { get; init; } = string.Empty;
        public uint Location { get; init; }
        public uint ByteSize { get; init; }
        public uint Alignment { get; init; }
        public string FormatHint { get; init; } = string.Empty;
        public string ScalarType { get; init; } = string.Empty;
        public uint ComponentCount { get; init; }
        public uint ComponentOffset { get; set; }
        public uint ByteOffset { get; set; }
    }

    private sealed class VertexInputSlot
    {
        public string FieldName { get; init; } = string.Empty;
        public string ScalarType { get; init; } = string.Empty;
        public uint Location { get; init; }
        public uint ComponentCount { get; set; }
        public uint ByteOffset { get; set; }
        public List<VertexInputCandidate> Members { get; } = [];
    }

    private static bool TryBuildStruct(INamedTypeSymbol type, ModuleCompilationContext context, Dictionary<INamedTypeSymbol, ShaderIrStruct> definitions, HashSet<INamedTypeSymbol> visiting, out ShaderIrStruct? structure, out string? reason)
    {
        if (definitions.TryGetValue(type, out var existing)) { structure = existing; reason = null; return true; }
        if (ShaderStructSupport.IsStateless(type)) { structure = null; reason = null; return true; }
        if (!visiting.Add(type)) { structure = null; reason = $"Recursive shader struct '{type.ToDisplayString()}' is not supported."; return false; }
        var layout = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layout?.ConstructorArguments.FirstOrDefault().Value is int kind && (kind == 2 || kind == 3))
        { visiting.Remove(type); structure = null; reason = $"Shader struct '{type.ToDisplayString()}' uses explicit or auto layout."; return false; }
        var members = new List<ShaderIrStructMember>();
        uint offset = 0, alignment = 1;
        foreach (var member in GetStructValueMembers(type))
        {
            var memberType = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
            if (memberType is INamedTypeSymbol statelessType &&
                statelessType.TypeKind == TypeKind.Struct &&
                !TryMapType(statelessType, context, out _) &&
                ShaderStructSupport.IsStateless(statelessType))
            {
                continue;
            }

            if (!TryMapType(memberType, context, out var glslType) && memberType is INamedTypeSymbol nested && nested.TypeKind == TypeKind.Struct)
            {
                if (!TryBuildStruct(nested, context, definitions, visiting, out var nestedStruct, out reason) || nestedStruct is null) { structure = null; visiting.Remove(type); return false; }
                glslType = nestedStruct.GlslName;
                var nestedLayout = ShaderStd430Layout.ForStruct(nestedStruct.Alignment, nestedStruct.Size);
                offset = AlignUp(offset, nestedLayout.Alignment);
                members.Add(new ShaderIrStructMember { Name = member.Name, GlslName = "member_" + Sanitize(member.Name), GlslType = glslType, Offset = offset, Alignment = nestedLayout.Alignment, Size = nestedLayout.Size, ArrayStride = nestedLayout.ArrayStride, Members = nestedStruct.Members });
                offset += nestedLayout.Size; alignment = nestedLayout.Alignment > alignment ? nestedLayout.Alignment : alignment; continue;
            }
            if (string.IsNullOrEmpty(glslType)) { structure = null; visiting.Remove(type); reason = $"Shader struct member '{member.Name}' has unsupported type '{memberType}'."; return false; }
            var fieldLayout = ShaderStd430Layout.ForGlslType(glslType);
            offset = AlignUp(offset, fieldLayout.Alignment);
            members.Add(new ShaderIrStructMember { Name = member.Name, GlslName = "member_" + Sanitize(member.Name), GlslType = glslType, Offset = offset, Alignment = fieldLayout.Alignment, Size = fieldLayout.Size, ArrayStride = fieldLayout.ArrayStride, MatrixStride = fieldLayout.MatrixStride });
            offset += fieldLayout.Size; alignment = fieldLayout.Alignment > alignment ? fieldLayout.Alignment : alignment;
        }
        if (members.Count == 0) { structure = null; visiting.Remove(type); reason = $"Shader struct '{type.ToDisplayString()}' has no instance data fields."; return false; }
        structure = new ShaderIrStruct { Name = type.ToDisplayString(), GlslName = "DeltaStruct_" + Sanitize(type.ToDisplayString()), Alignment = alignment, Size = AlignUp(offset, alignment), ArrayStride = AlignUp(offset, alignment), Members = members };
        definitions[type] = structure; visiting.Remove(type); reason = null; return true;
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

    private static uint AlignUp(uint value, uint alignment) => alignment == 0 ? value : (value + alignment - 1) / alignment * alignment;
    private static string Sanitize(string value) => new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
}
