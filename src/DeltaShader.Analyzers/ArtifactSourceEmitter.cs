using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Delta.Shader;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Analyzers;

internal static partial class ArtifactSourceEmitter
{
    public static bool TryEmitPackingMethods(
        IMethodSymbol method,
        ShaderCompilationManifest manifest,
        out string source,
        out string? reason)
    {
        source = string.Empty;
        reason = null;
        if (method.Parameters.Length != 1)
        {
            reason = "A generated packer requires the single shader context parameter.";
            return false;
        }

        var contextType = method.Parameters[0].Type;
        var methods = new StringBuilder();
        var stem = SanitizeIdentifier(method.Name);
        var storageResources = manifest.Resources
            .Where(resource => resource.Category == "storage-buffer")
            .ToArray();

        foreach (var pushConstant in manifest.PushConstants)
        {
            if (pushConstant.Size == 0)
            {
                continue;
            }

            var rootType = (ITypeSymbol)contextType;
            var rootExpression = "value";
            ISymbol? rootField = null;
            var namedPushField = FindValueMember(contextType, pushConstant.ParameterName);
            if (namedPushField is not null && GetMemberType(namedPushField) is INamedTypeSymbol namedPushType &&
                pushConstant.Members.Count > 0 &&
                pushConstant.Members.All(member => FindValueMember(namedPushType, member.Name) is not null))
            {
                rootType = namedPushType;
                rootExpression = "value." + namedPushField.Name;
                rootField = namedPushField;
            }

            var contextOperations = new StringBuilder();
            if (!TryEmitMembers(pushConstant.Members, rootType, rootExpression, 0u, contextOperations, out reason))
            {
                return false;
            }

            AppendPackMethod(
                methods,
                "Pack" + stem + "Context",
                contextType,
                pushConstant.Size,
                contextOperations);

            if (rootField is not null)
            {
                var rootOperations = new StringBuilder();
                if (!TryEmitMembers(pushConstant.Members, rootType, "value", 0u, rootOperations, out reason))
                {
                    return false;
                }

                AppendPackMethod(
                    methods,
                    "Pack" + stem + SanitizeIdentifier(rootField.Name),
                    rootType,
                    pushConstant.Size,
                    rootOperations);

                if (TryBuildUnpackMembersExpression(pushConstant.Members, rootType, 0u, out var unpackExpression, out _))
                {
                    AppendUnpackMethod(
                        methods,
                        "Unpack" + stem + "PushConstants",
                        rootType,
                        pushConstant.Size,
                        unpackExpression);
                }
            }
            else if (pushConstant.Members.Count == 1 &&
                     pushConstant.Members[0] is { Offset: 0u } rootMember &&
                     rootMember.Size == pushConstant.Size &&
                     FindValueMember(contextType, rootMember.Name) is ISymbol contextRootField &&
                     GetMemberType(contextRootField) is ITypeSymbol contextRootType)
            {
                var directRootOperations = new StringBuilder();
                if (!TryEmitValue(rootMember, contextRootType, "value", 0u, directRootOperations, out reason))
                {
                    return false;
                }

                AppendPackMethod(
                    methods,
                    "Pack" + stem + SanitizeIdentifier(contextRootField.Name),
                    contextRootType,
                    pushConstant.Size,
                    directRootOperations);

                if (TryBuildUnpackValueExpression(rootMember, contextRootType, 0u, out var unpackExpression, out _))
                {
                    AppendUnpackMethod(
                        methods,
                        "Unpack" + stem + "PushConstants",
                        contextRootType,
                        pushConstant.Size,
                        unpackExpression);
                }
            }
        }

        foreach (var resource in storageResources)
        {
            if (resource.Size == 0 || resource.ArrayStride == 0)
            {
                reason = $"Storage-buffer '{resource.Name}' has no resolved std430 element stride.";
                return false;
            }

            var resourceFieldName = resource.ParameterName.Split('.').Last();
            var resourceField = FindValueMember(contextType, resourceFieldName);
            if (resourceField is null || GetMemberType(resourceField) is not INamedTypeSymbol resourceType || resourceType.TypeArguments.Length != 1)
            {
                reason = $"Could not resolve the element type for storage-buffer '{resource.Name}'.";
                return false;
            }

            var elementType = resourceType.TypeArguments[0];
            var elementMember = new ShaderCompilationMember
            {
                Name = resource.Name,
                GlslType = resource.GlslType ?? string.Empty,
                Size = resource.Size,
                Alignment = resource.Alignment,
                ArrayStride = resource.ArrayStride,
                MatrixStride = resource.MatrixStride,
                Members = resource.Members
            };
            var operations = new StringBuilder();
            if (!TryEmitValue(elementMember, elementType, "value", 0u, operations, out reason))
            {
                return false;
            }

            var elementName = "Pack" + stem + SanitizeIdentifier(resource.Name) + "Element";
            AppendPackMethod(methods, elementName, elementType, resource.Size, operations);
            var canUnpackElement = TryBuildUnpackValueExpression(elementMember, elementType, 0u, out var unpackExpression, out _);
            if (canUnpackElement)
            {
                AppendUnpackMethod(
                    methods,
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Element",
                    elementType,
                    resource.Size,
                    unpackExpression);
            }

            if (!TryAppendArrayPackMethods(methods, elementName, "Pack" + stem + SanitizeIdentifier(resource.Name) + "Elements", elementType, resource.ArrayStride, out reason))
            {
                return false;
            }

            if (canUnpackElement && !TryAppendArrayUnpackMethods(
                    methods,
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Element",
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Elements",
                    elementType,
                    resource.ArrayStride,
                    out reason))
            {
                return false;
            }
        }

        if (!BufferRangePlanSourceEmitter.TryAppendStorageBufferRangeMethods(
                methods,
                stem,
                storageResources,
                out reason))
        {
            return false;
        }

        if (method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName))
        {
            if (!TryAppendVertexPackMethods(method, manifest, contextType, methods, stem, out reason) ||
                !BufferRangePlanSourceEmitter.TryAppendVertexBufferRangeMethods(methods, stem, manifest.VertexBufferBindings, out reason) ||
                !BufferRangePlanSourceEmitter.TryAppendSharedBufferRangeMethods(
                    methods,
                    stem,
                    storageResources,
                    manifest.VertexBufferBindings,
                    out reason))
            {
                return false;
            }
        }

        source = methods.ToString();
        return true;
    }

    private static bool TryAppendVertexPackMethods(
        IMethodSymbol method,
        ShaderCompilationManifest manifest,
        ITypeSymbol contextType,
        StringBuilder source,
        string stem,
        out string? reason)
    {
        reason = null;
        if (manifest.VertexInputs.Count == 0)
        {
            return true;
        }

        if (manifest.VertexBufferBindings.Count == 0)
        {
            reason = $"Vertex shader '{method.Name}' has vertex inputs but no resolved buffer bindings.";
            return false;
        }

        var varyingField = contextType is INamedTypeSymbol namedContext
            ? namedContext.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field =>
                !field.IsStatic && IsInterstagePayloadField(field))
            : null;
        if (varyingField is null || varyingField.Type is not INamedTypeSymbol varyingType)
        {
            reason = $"Vertex shader '{method.Name}' has no resolvable interstage payload type.";
            return false;
        }

        var bindings = manifest.VertexBufferBindings
            .OrderBy(binding => binding.Binding)
            .ToArray();
        for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            var binding = bindings[bindingIndex];
            if (binding.Stride == 0 || binding.Stride > int.MaxValue)
            {
                reason = $"Vertex shader '{method.Name}' has no valid stride for binding {binding.Binding}.";
                return false;
            }

            var bindingInputs = manifest.VertexInputs
                .Where(input => input.Binding == binding.Binding)
                .ToArray();
            if (bindingInputs.Length == 0)
            {
                continue;
            }

            var operations = new StringBuilder();
            foreach (var input in bindingInputs)
            {
                var member = new ShaderCompilationMember
                {
                    Name = input.ParameterName,
                    GlslType = input.GlslType,
                    Offset = input.ByteOffset,
                    Size = input.ByteSize,
                    Alignment = input.Alignment,
                    ArrayStride = input.ByteSize
                };
                var payloadMember = FindValueMember(varyingType, input.ParameterName);
                if (payloadMember is null || GetMemberType(payloadMember) is not ITypeSymbol payloadMemberType)
                {
                    reason = $"Could not resolve vertex input field '{input.ParameterName}'.";
                    return false;
                }

                var payloadExpression = "value." + payloadMember.Name;
                if (!TryEmitValue(member, payloadMemberType, payloadExpression, 0u, operations, out reason))
                {
                    return false;
                }
            }

            var suffix = bindings.Length == 1 && binding.Binding == 0u
                ? "Vertex"
                : "VertexBinding" + binding.Binding.ToString(CultureInfo.InvariantCulture);
            var elementName = "Pack" + stem + suffix + "Element";
            AppendPackMethod(source, elementName, varyingType, binding.Stride, operations);
            if (!TryAppendArrayPackMethods(
                    source,
                    elementName,
                    "Pack" + stem + suffix + "Elements",
                    varyingType,
                    binding.Stride,
                    out reason))
            {
                return false;
            }

            var unpackMembers = bindingInputs.Select(input => new ShaderCompilationMember
            {
                Name = input.ParameterName,
                GlslType = input.GlslType,
                Offset = input.ByteOffset,
                Size = input.ByteSize,
                Alignment = input.Alignment,
                ArrayStride = input.ByteSize
            }).ToArray();
            if (TryBuildUnpackMembersExpression(unpackMembers, varyingType, 0u, out var unpackExpression, out _))
            {
                AppendUnpackMethod(
                    source,
                    "Unpack" + stem + suffix + "Element",
                    varyingType,
                    binding.Stride,
                    unpackExpression);

                if (!TryAppendArrayUnpackMethods(
                        source,
                        "Unpack" + stem + suffix + "Element",
                        "Unpack" + stem + suffix + "Elements",
                        varyingType,
                        binding.Stride,
                        out reason))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryEmitMembers(
        IReadOnlyList<ShaderCompilationMember> members,
        ITypeSymbol containingType,
        string expression,
        uint baseOffset,
        StringBuilder operations,
        out string? reason)
    {
        foreach (var member in members)
        {
            var symbol = FindValueMember(containingType, member.Name);
            if (symbol is null || GetMemberType(symbol) is not ITypeSymbol memberType)
            {
                reason = $"Could not resolve ABI member '{member.Name}' on '{containingType.Name}'.";
                return false;
            }

            var memberExpression = expression + "." + member.Name;
            if (member.Members.Count > 0)
            {
                if (!TryEmitMembers(member.Members, memberType, memberExpression, baseOffset + member.Offset, operations, out reason))
                {
                    return false;
                }
            }
            else if (!TryEmitLeaf(member.GlslType, memberType, memberExpression, baseOffset + member.Offset, member.MatrixStride, operations, out reason))
            {
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool TryBuildUnpackValueExpression(
        ShaderCompilationMember member,
        ITypeSymbol valueType,
        uint baseOffset,
        out string expression,
        out string? reason)
    {
        if (TryGetSemanticValueType(valueType, out var semanticValueType))
        {
            if (!TryBuildUnpackLeafExpression(
                    member.GlslType,
                    semanticValueType,
                    baseOffset + member.Offset,
                    member.MatrixStride,
                    out var underlyingExpression,
                    out reason))
            {
                expression = string.Empty;
                return false;
            }

            expression = "new " + FullyQualifiedType(valueType) + "(" + underlyingExpression + ")";
            return true;
        }

        if (member.Members.Count > 0)
        {
            return TryBuildUnpackMembersExpression(member.Members, valueType, baseOffset, out expression, out reason);
        }

        return TryBuildUnpackLeafExpression(
            member.GlslType,
            valueType,
            baseOffset + member.Offset,
            member.MatrixStride,
            out expression,
            out reason);
    }

    private static bool TryBuildUnpackMembersExpression(
        IReadOnlyList<ShaderCompilationMember> members,
        ITypeSymbol containingType,
        uint baseOffset,
        out string expression,
        out string? reason)
    {
        var assignments = new List<string>(members.Count);
        foreach (var member in members)
        {
            var symbol = FindWritableMember(containingType, member.Name);
            if (symbol is null || GetMemberType(symbol) is not ITypeSymbol memberType)
            {
                expression = string.Empty;
                reason = $"Could not resolve an initializable ABI member '{member.Name}' on '{containingType.Name}'.";
                return false;
            }

            if (!TryBuildUnpackValueExpression(member, memberType, baseOffset, out var memberExpression, out reason))
            {
                expression = string.Empty;
                return false;
            }

            assignments.Add(member.Name + " = " + memberExpression);
        }

        expression = "new " + FullyQualifiedType(containingType) + " { " + string.Join(", ", assignments) + " }";
        reason = null;
        return true;
    }

    private static bool TryBuildUnpackLeafExpression(
        string glslType,
        ITypeSymbol valueType,
        uint offset,
        uint? matrixStride,
        out string expression,
        out string? reason)
    {
        if (glslType is "bool" or "int" or "uint" or "float" or "double" or "float16_t")
        {
            var reader = glslType switch
            {
                "bool" => "ReadBool",
                "int" => "ReadInt",
                "uint" => "ReadUInt",
                "double" => "ReadDouble",
                "float16_t" => "ReadHalf",
                _ => "ReadFloat"
            };
            var castType = glslType switch
            {
                "bool" => "bool",
                "int" => "int",
                "uint" => "uint",
                "double" => "double",
                _ => "float"
            };
            expression = glslType == "float16_t"
                ? $"new global::Delta.Maths.half(reader.{reader}({offset}u))"
                : ScalarExpression($"reader.{reader}({offset}u)", valueType, castType);
            reason = null;
            return true;
        }

        if (TryGetVectorType(glslType, out var vectorWriter, out var componentCount, out var componentSize))
        {
            var vectorReader = vectorWriter switch
            {
                "WriteBool" => "ReadBool",
                "WriteInt" => "ReadInt",
                "WriteUInt" => "ReadUInt",
                "WriteDouble" => "ReadDouble",
                "WriteHalf" => "ReadHalf",
                _ => "ReadFloat"
            };
            var components = new string[componentCount];
            for (var index = 0; index < componentCount; index++)
            {
                var componentOffset = offset + (uint)index * componentSize;
                components[index] = glslType.StartsWith("f16vec", StringComparison.Ordinal)
                    ? $"new global::Delta.Maths.half(reader.{vectorReader}({componentOffset}u))"
                    : $"reader.{vectorReader}({componentOffset}u)";
            }

            expression = "new " + FullyQualifiedType(valueType) + "(" + string.Join(", ", components) + ")";
            reason = null;
            return true;
        }

        if (TryGetMatrixType(glslType, out var columns, out var rows))
        {
            var stride = matrixStride ?? (rows == 2 ? 8u : 16u);
            var components = new[] { "x", "y", "z", "w" };
            var assignments = new List<string>(columns);
            for (var column = 0; column < columns; column++)
            {
                var columnName = "c" + column.ToString(CultureInfo.InvariantCulture);
                var columnSymbol = FindWritableMember(valueType, columnName);
                if (columnSymbol is null || GetMemberType(columnSymbol) is not ITypeSymbol columnType)
                {
                    expression = string.Empty;
                    reason = $"Could not resolve an initializable matrix column '{columnName}' on '{valueType.Name}'.";
                    return false;
                }

                var values = new string[rows];
                for (var row = 0; row < rows; row++)
                {
                    var componentOffset = offset + (uint)column * stride + (uint)row * ScalarByteWidth(glslType);
                    values[row] = glslType.StartsWith("f16mat", StringComparison.Ordinal)
                        ? $"new global::Delta.Maths.half(reader.ReadHalf({componentOffset}u))"
                        : $"reader.{ScalarReader(glslType)}({componentOffset}u)";
                }

                var columnTypeName = FullyQualifiedType(columnType);
                assignments.Add(columnName + " = new " + columnTypeName + "(" + string.Join(", ", values) + ")");
            }

            expression = "new " + FullyQualifiedType(valueType) + " { " + string.Join(", ", assignments) + " }";
            reason = null;
            return true;
        }

        expression = string.Empty;
        reason = $"GLSL value '{glslType}' has no generated std430 unpacking implementation.";
        return false;
    }

    private static bool TryEmitValue(
        ShaderCompilationMember member,
        ITypeSymbol valueType,
        string expression,
        uint baseOffset,
        StringBuilder operations,
        out string? reason)
    {
        if (TryGetSemanticValueType(valueType, out var semanticValueType))
        {
            expression += ".Value";
            valueType = semanticValueType;
        }

        if (member.Members.Count > 0)
        {
            return TryEmitMembers(member.Members, valueType, expression, baseOffset, operations, out reason);
        }

        return TryEmitLeaf(member.GlslType, valueType, expression, baseOffset + member.Offset, member.MatrixStride, operations, out reason);
    }

    private static bool TryEmitLeaf(
        string glslType,
        ITypeSymbol valueType,
        string expression,
        uint offset,
        uint? matrixStride,
        StringBuilder operations,
        out string? reason)
    {
        if (glslType is "bool" or "int" or "uint" or "float" or "double" or "float16_t")
        {
            var writer = glslType switch
            {
                "bool" => "WriteBool",
                "int" => "WriteInt",
                "uint" => "WriteUInt",
                "double" => "WriteDouble",
                "float16_t" => "WriteHalf",
                _ => "WriteFloat"
            };
            var castType = glslType switch
            {
                "bool" => "bool",
                "int" => "int",
                "uint" => "uint",
                "double" => "double",
                _ => "float"
            };
            var scalarExpression = glslType == "float16_t"
                ? expression + ".raw"
                : ScalarExpression(expression, valueType, castType);
            operations.AppendLine($"        writer.{writer}({offset}u, {scalarExpression});");
            reason = null;
            return true;
        }

        if (TryGetVectorType(glslType, out var vectorWriter, out var componentCount, out var componentSize))
        {
            var components = new[] { "x", "y", "z", "w" };
            for (var index = 0; index < componentCount; index++)
            {
                var componentOffset = offset + (uint)index * componentSize;
                var componentExpression = expression + "." + components[index];
                if (glslType.StartsWith("f16vec", StringComparison.Ordinal))
                {
                    componentExpression += ".raw";
                }

                operations.AppendLine($"        writer.{vectorWriter}({componentOffset}u, {componentExpression});");
            }

            reason = null;
            return true;
        }

        if (TryGetMatrixType(glslType, out var columns, out var rows))
        {
            var stride = matrixStride ?? (rows == 2 ? 8u : 16u);
            var components = new[] { "x", "y", "z", "w" };
            for (var column = 0; column < columns; column++)
            {
                for (var row = 0; row < rows; row++)
                {
                    var componentOffset = offset + (uint)column * stride + (uint)row * ScalarByteWidth(glslType);
                    var componentExpression = expression + $".c{column}." + components[row];
                    if (glslType.StartsWith("f16mat", StringComparison.Ordinal))
                    {
                        componentExpression += ".raw";
                    }

                    operations.AppendLine($"        writer.{ScalarWriter(glslType)}({componentOffset}u, {componentExpression});");
                }
            }

            reason = null;
            return true;
        }

        reason = $"GLSL value '{glslType}' has no generated std430 packing implementation.";
        return false;
    }

    private static bool TryGetVectorType(
        string glslType,
        out string writer,
        out int componentCount,
        out uint componentSize)
    {
        writer = string.Empty;
        componentCount = 0;
        componentSize = 0;
        var prefixLength = glslType.StartsWith("vec", StringComparison.Ordinal) ? 3 :
            glslType.StartsWith("ivec", StringComparison.Ordinal) ||
            glslType.StartsWith("uvec", StringComparison.Ordinal) ||
            glslType.StartsWith("bvec", StringComparison.Ordinal) ? 4 :
            glslType.StartsWith("dvec", StringComparison.Ordinal) ? 4 :
            glslType.StartsWith("f16vec", StringComparison.Ordinal) ? 6 : 0;
        if (prefixLength == 0 || glslType.Length != prefixLength + 1 || glslType[glslType.Length - 1] is < '2' or > '4')
        {
            return false;
        }

        componentCount = glslType[glslType.Length - 1] - '0';
        writer = glslType.Substring(0, prefixLength) switch
        {
            "ivec" => "WriteInt",
            "uvec" => "WriteUInt",
            "bvec" => "WriteBool",
            "vec" => "WriteFloat",
            "dvec" => "WriteDouble",
            "f16vec" => "WriteHalf",
            _ => string.Empty
        };
        componentSize = glslType.StartsWith("dvec", StringComparison.Ordinal)
            ? 8u
            : glslType.StartsWith("f16vec", StringComparison.Ordinal)
                ? 2u
                : 4u;
        return writer.Length > 0;
    }

    private static bool TryGetMatrixType(string glslType, out int columns, out int rows)
    {
        columns = 0;
        rows = 0;
        var prefixLength = glslType.StartsWith("f16mat", StringComparison.Ordinal) ? 6 :
            glslType.StartsWith("dmat", StringComparison.Ordinal) ? 4 :
            glslType.StartsWith("mat", StringComparison.Ordinal) ? 3 : 0;
        if (prefixLength == 0)
        {
            return false;
        }

        var dimensions = glslType.Substring(prefixLength);
        if (dimensions.Length == 1 && dimensions[0] is >= '2' and <= '4')
        {
            columns = rows = dimensions[0] - '0';
            return true;
        }

        var separator = dimensions.IndexOf('x');
        return separator == 1 && dimensions.Length == 3 &&
            int.TryParse(dimensions[0].ToString(), out columns) &&
            int.TryParse(dimensions[2].ToString(), out rows);
    }

    private static uint ScalarByteWidth(string glslType)
        => glslType.StartsWith("f16", StringComparison.Ordinal)
            ? 2u
            : glslType.StartsWith("d", StringComparison.Ordinal)
                ? 8u
                : 4u;

    private static string ScalarWriter(string glslType)
        => glslType.StartsWith("f16", StringComparison.Ordinal)
            ? "WriteHalf"
            : glslType.StartsWith("d", StringComparison.Ordinal)
                ? "WriteDouble"
                : "WriteFloat";

    private static string ScalarReader(string glslType)
        => glslType.StartsWith("f16", StringComparison.Ordinal)
            ? "ReadHalf"
            : glslType.StartsWith("d", StringComparison.Ordinal)
                ? "ReadDouble"
                : "ReadFloat";

    private static string ScalarExpression(string expression, ITypeSymbol type, string targetType)
        => type.TypeKind == TypeKind.Enum ? $"({targetType})({expression})" : expression;

    private static bool TryGetSemanticValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        if (type is INamedTypeSymbol namedType &&
            IsSemanticType(namedType) &&
            namedType.GetMembers("Value").OfType<IFieldSymbol>().SingleOrDefault() is IFieldSymbol valueField)
        {
            valueType = valueField.Type;
            return true;
        }

        valueType = type;
        return false;
    }

    private static bool IsSemanticType(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
            "global::Delta.Shader.Position" or
            "global::Delta.Shader.Uv0" or
            "global::Delta.Shader.Uv1" or
            "global::Delta.Shader.Color" or
            "global::Delta.Shader.VertexColor" or
            "global::Delta.Shader.FragmentColor" or
            "global::Delta.Shader.WorldPosition" or
            "global::Delta.Shader.WorldNormal" or
            "global::Delta.Shader.Tangent" or
            "global::Delta.Shader.Pixel" or
            "global::Delta.Shader.SegmentRect" or
            "global::Delta.Shader.CornerData" or
            "global::Delta.Shader.CornerRadii" or
            "global::Delta.Shader.BorderWidth";

    private static bool IsInterstagePayloadField(IFieldSymbol field)
        => field.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == typeof(InterstageAttribute).FullName) ||
            field.Type is INamedTypeSymbol payloadType &&
            payloadType.GetMembers().OfType<IFieldSymbol>().Any(payloadField =>
                !payloadField.IsStatic && IsSemanticType(payloadField.Type));

    private static ISymbol? FindValueMember(ITypeSymbol type, string name)
        => type is INamedTypeSymbol namedType
            ? namedType.GetMembers(name).FirstOrDefault(member =>
                member switch
                {
                    IFieldSymbol field => !field.IsStatic,
                    IPropertySymbol property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null,
                    _ => false
                })
            : null;

    private static ISymbol? FindWritableMember(ITypeSymbol type, string name)
        => type is INamedTypeSymbol namedType
            ? namedType.GetMembers(name).FirstOrDefault(member =>
                member switch
                {
                    IFieldSymbol field => !field.IsStatic && !field.IsReadOnly,
                    IPropertySymbol property => !property.IsStatic && !property.IsIndexer && property.SetMethod is not null,
                    _ => false
                })
            : null;

    private static ITypeSymbol? GetMemberType(ISymbol member)
        => member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

    private static string AccessibilityModifier(ITypeSymbol type)
        => IsPubliclyAccessible(type) ? "public" : "internal";

    private static bool IsPubliclyAccessible(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsPubliclyAccessible(arrayType.ElementType);
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return IsPubliclyAccessible(pointerType.PointedAtType);
        }

        if (type is ITypeParameterSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol namedType || namedType.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        for (var containingType = namedType.ContainingType;
             containingType is not null;
             containingType = containingType.ContainingType)
        {
            if (containingType.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return namedType.TypeArguments.All(IsPubliclyAccessible);
    }

    private static bool TryAppendArrayPackMethods(
        StringBuilder source,
        string elementName,
        string arrayName,
        ITypeSymbol elementType,
        uint stride,
        out string? reason)
    {
        if (stride > int.MaxValue)
        {
            reason = "The resolved array stride exceeds the managed span index range.";
            return false;
        }

        var accessibility = AccessibilityModifier(elementType);
        var typeName = FullyQualifiedType(elementType);
        var strideText = stride.ToString(CultureInfo.InvariantCulture);
        source.Append($$"""
                {{accessibility}} static int {{arrayName}}(ReadOnlySpan<{{typeName}}> values, Span<byte> destination)
                {
                    int required = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {{stride}}u);
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, (uint)required);
                    destination.Slice(0, required).Clear();
                    for (int index = 0; index < values.Length; index++)
                    {
                        {{elementName}}(in values[index], destination.Slice(checked(index * {{strideText}}), {{strideText}}));
                    }
                    return required;
                }

                {{accessibility}} static byte[] {{arrayName}}(ReadOnlySpan<{{typeName}}> values)
                {
                    var result = new byte[Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {{stride}}u)];
                    {{arrayName}}(values, result);
                    return result;
                }

            """);
        reason = null;
        return true;
    }

    private static bool TryAppendArrayUnpackMethods(
        StringBuilder source,
        string elementName,
        string arrayName,
        ITypeSymbol elementType,
        uint stride,
        out string? reason)
    {
        if (stride > int.MaxValue)
        {
            reason = "The resolved array stride exceeds the managed span index range.";
            return false;
        }

        var accessibility = AccessibilityModifier(elementType);
        var typeName = FullyQualifiedType(elementType);
        var strideText = stride.ToString(CultureInfo.InvariantCulture);
        source.Append($$"""
                {{accessibility}} static int {{arrayName}}(ReadOnlySpan<byte> source, Span<{{typeName}}> values)
                {
                    int required = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {{stride}}u);
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(source, (uint)required);
                    for (int index = 0; index < values.Length; index++)
                    {
                        values[index] = {{elementName}}(source.Slice(checked(index * {{strideText}}), {{strideText}}));
                    }
                    return required;
                }

                {{accessibility}} static {{typeName}}[] {{arrayName}}(ReadOnlySpan<byte> source)
                {
                    const int stride = {{strideText}};
                    if (source.Length % stride != 0)
                    {
                        throw new ArgumentException("The source length must be a multiple of the resolved std430 array stride.", nameof(source));
                    }
                    var result = new {{typeName}}[source.Length / stride];
                    {{arrayName}}(source, result);
                    return result;
                }

            """);
        reason = null;
        return true;
    }

    private static void AppendPackMethod(
        StringBuilder methods,
        string name,
        ITypeSymbol type,
        uint size,
        StringBuilder operations)
    {
        var sizeText = size.ToString(CultureInfo.InvariantCulture);
        var accessibility = AccessibilityModifier(type);
        var typeName = FullyQualifiedType(type);
        methods.Append($$"""
                {{accessibility}} static int {{name}}(in {{typeName}} value, Span<byte> destination)
                {
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, {{size}}u);
                    destination.Slice(0, {{sizeText}}).Clear();
                    var writer = new Delta.Shader.Packing.Std430Writer(destination);
                    {{operations}}
                    return {{sizeText}};
                }

                {{accessibility}} static byte[] {{name}}(in {{typeName}} value)
                {
                    var result = new byte[{{sizeText}}];
                    {{name}}(in value, result);
                    return result;
                }

            """);
    }

    private static void AppendUnpackMethod(
        StringBuilder methods,
        string name,
        ITypeSymbol type,
        uint size,
        string expression)
    {
        var sizeText = size.ToString(CultureInfo.InvariantCulture);
        var accessibility = AccessibilityModifier(type);
        var typeName = FullyQualifiedType(type);
        methods.Append($$"""
                {{accessibility}} static {{typeName}} {{name}}(ReadOnlySpan<byte> source)
                {
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(source, {{size}}u);
                    var reader = new Delta.Shader.Packing.Std430Reader(source);
                    {{typeName}} value = {{expression}};
                    return value;
                }

            """);
    }

    private static string FullyQualifiedType(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string SanitizeIdentifier(string name)
        => string.Concat(name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')) is { Length: > 0 } value
            ? value
            : "Value";

}
