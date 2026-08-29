using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Delta.Shader;
using Delta.Shader.Compiler;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Analyzers;

internal static class ArtifactSourceEmitter
{
    public static string EmitAbiFactory(ShaderCompilationManifest manifest)
    {
        var source = new StringBuilder();
        source.AppendLine("    private static Delta.Shader.Contract.ShaderAbi CreateAbi()");
        source.AppendLine("    {");
        source.AppendLine("        return new Delta.Shader.Contract.ShaderAbi(");
        source.AppendLine($"            stage: {Stage(manifest.Stage)},");
        source.AppendLine($"            resources: {Resources(manifest.Resources)},");
        source.AppendLine($"            pushConstants: {PushConstants(manifest.Stage, manifest.PushConstants)},");
        source.AppendLine($"            inputs: {Interfaces(manifest.Inputs)},");
        source.AppendLine($"            outputs: {Interfaces(manifest.Outputs)},");
        source.AppendLine($"            vertexInputs: {VertexInputs(manifest.VertexInputs)},");
        source.AppendLine($"            vertexBuffers: {VertexBuffers(manifest.VertexBufferBindings)},");
        source.AppendLine("            workgroupSize: ");
        source.AppendLine($"                {(manifest.Stage == ShaderStage.Compute ? Workgroup(manifest) : "default")},");
        source.AppendLine("            requiredCapabilities: Delta.Shader.Contract.ShaderCapabilities.None);");
        source.AppendLine("    }");
        return source.ToString();
    }

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

        foreach (var pushConstant in manifest.PushConstants)
        {
            if (pushConstant.Size == 0)
            {
                continue;
            }

            if (!TryGetPushRoot(contextType, pushConstant, out var rootExpression, out var rootType, out var rootField))
            {
                reason = $"Could not resolve the push-constant source for '{method.Name}'.";
                return false;
            }

            var operations = new StringBuilder();
            if (!TryEmitMembers(pushConstant.Members, rootType, rootExpression, 0u, operations, out reason))
            {
                return false;
            }

            AppendPackMethod(
                methods,
                "Pack" + stem + "Context",
                FullyQualifiedType(contextType),
                pushConstant.Size,
                operations);

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
                    FullyQualifiedType(rootType),
                    pushConstant.Size,
                    rootOperations);

                var unpackOperations = new StringBuilder();
                if (TryEmitUnpackMembers(pushConstant.Members, rootType, "value", 0u, unpackOperations, out _))
                {
                    AppendUnpackMethod(
                        methods,
                        "Unpack" + stem + "PushConstants",
                        FullyQualifiedType(rootType),
                        pushConstant.Size,
                        unpackOperations);
                }
            }
        }

        foreach (var resource in manifest.Resources.Where(resource => resource.Category == "storage-buffer"))
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
            AppendPackMethod(methods, elementName, FullyQualifiedType(elementType), resource.Size, operations);
            var unpackOperations = new StringBuilder();
            var canUnpackElement = TryEmitUnpackValue(elementMember, elementType, "value", 0u, unpackOperations, out _);
            if (canUnpackElement)
            {
                AppendUnpackMethod(
                    methods,
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Element",
                    FullyQualifiedType(elementType),
                    resource.Size,
                    unpackOperations);
            }

            if (!TryAppendArrayPackMethods(methods, elementName, "Pack" + stem + SanitizeIdentifier(resource.Name) + "Elements", FullyQualifiedType(elementType), resource.ArrayStride, out reason))
            {
                return false;
            }

            if (canUnpackElement && !TryAppendArrayUnpackMethods(
                    methods,
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Element",
                    "Unpack" + stem + SanitizeIdentifier(resource.Name) + "Elements",
                    FullyQualifiedType(elementType),
                    resource.ArrayStride,
                    out reason))
            {
                return false;
            }
        }

        if (method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == typeof(VertexShaderAttribute).FullName) &&
            !TryAppendVertexPackMethods(method, manifest, contextType, methods, stem, out reason))
        {
            return false;
        }

        source = methods.ToString();
        return true;
    }

    private static bool TryGetPushRoot(
        ITypeSymbol contextType,
        ShaderCompilationPushConstant pushConstant,
        out string expression,
        out ITypeSymbol rootType,
        out ISymbol? rootField)
    {
        var namedPushField = FindValueMember(contextType, pushConstant.ParameterName);
        if (namedPushField is not null && GetMemberType(namedPushField) is INamedTypeSymbol namedPushType &&
            pushConstant.Members.Count > 0 && pushConstant.Members.All(member => FindValueMember(namedPushType, member.Name) is not null))
        {
            expression = "value." + namedPushField.Name;
            rootType = namedPushType;
            rootField = namedPushField;
            return true;
        }

        expression = "value";
        rootType = contextType;
        rootField = null;
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

        var binding = manifest.VertexBufferBindings.SingleOrDefault(candidate => candidate.Binding == 0u);
        if (binding is null || binding.Stride == 0 || binding.Stride > int.MaxValue)
        {
            reason = $"Vertex shader '{method.Name}' has no resolved binding-0 stride.";
            return false;
        }

        var varyingField = contextType is INamedTypeSymbol namedContext
            ? namedContext.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field =>
                !field.IsStatic && field.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == typeof(InterstageAttribute).FullName))
            : null;
        if (varyingField is null || varyingField.Type is not INamedTypeSymbol varyingType)
        {
            reason = $"Vertex shader '{method.Name}' has no resolvable interstage payload type.";
            return false;
        }

        var operations = new StringBuilder();
        foreach (var input in manifest.VertexInputs)
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

            if (!TryEmitValue(member, payloadMemberType, "value", 0u, operations, out reason))
            {
                return false;
            }
        }

        var elementName = "Pack" + stem + "VertexElement";
        AppendPackMethod(source, elementName, FullyQualifiedType(varyingType), binding.Stride, operations);
        if (!TryAppendArrayPackMethods(source, elementName, "Pack" + stem + "VertexElements", FullyQualifiedType(varyingType), binding.Stride, out reason))
        {
            return false;
        }

        var unpackOperations = new StringBuilder();
        var unpackMembers = manifest.VertexInputs.Select(input => new ShaderCompilationMember
        {
            Name = input.ParameterName,
            GlslType = input.GlslType,
            Offset = input.ByteOffset,
            Size = input.ByteSize,
            Alignment = input.Alignment,
            ArrayStride = input.ByteSize
        }).ToArray();
        if (TryEmitUnpackMembers(unpackMembers, varyingType, "value", 0u, unpackOperations, out _))
        {
            AppendUnpackMethod(
                source,
                "Unpack" + stem + "VertexElement",
                FullyQualifiedType(varyingType),
                binding.Stride,
                unpackOperations);

            return TryAppendArrayUnpackMethods(
                source,
                "Unpack" + stem + "VertexElement",
                "Unpack" + stem + "VertexElements",
                FullyQualifiedType(varyingType),
                binding.Stride,
                out reason);
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

    private static bool TryEmitUnpackValue(
        ShaderCompilationMember member,
        ITypeSymbol valueType,
        string expression,
        uint baseOffset,
        StringBuilder operations,
        out string? reason)
    {
        if (member.Members.Count > 0)
        {
            return TryEmitUnpackMembers(member.Members, valueType, expression, baseOffset, operations, out reason);
        }

        return TryEmitUnpackLeaf(member.GlslType, valueType, expression, baseOffset + member.Offset, member.MatrixStride, operations, out reason);
    }

    private static bool TryEmitUnpackMembers(
        IReadOnlyList<ShaderCompilationMember> members,
        ITypeSymbol containingType,
        string expression,
        uint baseOffset,
        StringBuilder operations,
        out string? reason)
    {
        foreach (var member in members)
        {
            var symbol = FindWritableMember(containingType, member.Name);
            if (symbol is null || GetMemberType(symbol) is not ITypeSymbol memberType)
            {
                reason = $"Could not resolve a writable ABI member '{member.Name}' on '{containingType.Name}'.";
                return false;
            }

            var memberExpression = expression + "." + member.Name;
            if (member.Members.Count > 0)
            {
                if (!TryEmitUnpackMembers(member.Members, memberType, memberExpression, baseOffset + member.Offset, operations, out reason))
                {
                    return false;
                }
            }
            else if (!TryEmitUnpackLeaf(member.GlslType, memberType, memberExpression, baseOffset + member.Offset, member.MatrixStride, operations, out reason))
            {
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool TryEmitUnpackLeaf(
        string glslType,
        ITypeSymbol valueType,
        string expression,
        uint offset,
        uint? matrixStride,
        StringBuilder operations,
        out string? reason)
    {
        if (glslType is "bool" or "int" or "uint" or "float")
        {
            var reader = glslType switch
            {
                "bool" => "ReadBool",
                "int" => "ReadInt",
                "uint" => "ReadUInt",
                _ => "ReadFloat"
            };
            var castType = glslType switch
            {
                "bool" => "bool",
                "int" => "int",
                "uint" => "uint",
                _ => "float"
            };
            var readExpression = $"reader.{reader}({offset}u)";
            operations.AppendLine($"        {expression} = {ScalarExpression(readExpression, valueType, castType)};");
            reason = null;
            return true;
        }

        if (TryGetVectorType(glslType, out var vectorWriter, out var componentCount))
        {
            var vectorReader = vectorWriter switch
            {
                "WriteBool" => "ReadBool",
                "WriteInt" => "ReadInt",
                "WriteUInt" => "ReadUInt",
                _ => "ReadFloat"
            };
            var components = new[] { "x", "y", "z", "w" };
            for (var index = 0; index < componentCount; index++)
            {
                var componentOffset = offset + (uint)(index * 4);
                operations.AppendLine($"        {expression}.{components[index]} = reader.{vectorReader}({componentOffset}u);");
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
                    var componentOffset = offset + (uint)column * stride + (uint)(row * 4);
                    operations.AppendLine($"        {expression}.c{column}.{components[row]} = reader.ReadFloat({componentOffset}u);");
                }
            }

            reason = null;
            return true;
        }

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
        if (glslType is "bool" or "int" or "uint" or "float")
        {
            var writer = glslType switch
            {
                "bool" => "WriteBool",
                "int" => "WriteInt",
                "uint" => "WriteUInt",
                _ => "WriteFloat"
            };
            var castType = glslType switch
            {
                "bool" => "bool",
                "int" => "int",
                "uint" => "uint",
                _ => "float"
            };
            operations.AppendLine($"        writer.{writer}({offset}u, {ScalarExpression(expression, valueType, castType)});");
            reason = null;
            return true;
        }

        if (TryGetVectorType(glslType, out var vectorWriter, out var componentCount))
        {
            var components = new[] { "x", "y", "z", "w" };
            for (var index = 0; index < componentCount; index++)
            {
                var componentOffset = offset + (uint)(index * 4);
                operations.AppendLine($"        writer.{vectorWriter}({componentOffset}u, {expression}.{components[index]});");
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
                    var componentOffset = offset + (uint)column * stride + (uint)(row * 4);
                    operations.AppendLine($"        writer.WriteFloat({componentOffset}u, {expression}.c{column}.{components[row]});");
                }
            }

            reason = null;
            return true;
        }

        reason = $"GLSL value '{glslType}' has no generated std430 packing implementation.";
        return false;
    }

    private static bool TryGetVectorType(string glslType, out string writer, out int componentCount)
    {
        writer = string.Empty;
        componentCount = 0;
        var prefixLength = glslType.StartsWith("vec", StringComparison.Ordinal) ? 3 :
            glslType.StartsWith("ivec", StringComparison.Ordinal) ||
            glslType.StartsWith("uvec", StringComparison.Ordinal) ||
            glslType.StartsWith("bvec", StringComparison.Ordinal) ? 4 : 0;
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
            _ => string.Empty
        };
        return writer.Length > 0;
    }

    private static bool TryGetMatrixType(string glslType, out int columns, out int rows)
    {
        columns = 0;
        rows = 0;
        if (!glslType.StartsWith("mat", StringComparison.Ordinal))
        {
            return false;
        }

        var dimensions = glslType.Substring(3);
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

    private static string ScalarExpression(string expression, ITypeSymbol type, string targetType)
        => type.TypeKind == TypeKind.Enum ? $"({targetType})({expression})" : expression;

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

    private static bool TryAppendArrayPackMethods(
        StringBuilder source,
        string elementName,
        string arrayName,
        string elementType,
        uint stride,
        out string? reason)
    {
        if (stride > int.MaxValue)
        {
            reason = "The resolved array stride exceeds the managed span index range.";
            return false;
        }

        var strideText = stride.ToString(CultureInfo.InvariantCulture);
        source.AppendLine($"    public static int {arrayName}(ReadOnlySpan<{elementType}> values, Span<byte> destination)");
        source.AppendLine("    {");
        source.AppendLine($"        int required = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {stride}u);");
        source.AppendLine("        Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, (uint)required);");
        source.AppendLine("        destination.Slice(0, required).Clear();");
        source.AppendLine("        for (int index = 0; index < values.Length; index++)");
        source.AppendLine("        {");
        source.AppendLine($"            {elementName}(in values[index], destination.Slice(checked(index * {strideText}), {strideText}));");
        source.AppendLine("        }");
        source.AppendLine("        return required;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    public static byte[] {arrayName}(ReadOnlySpan<{elementType}> values)");
        source.AppendLine("    {");
        source.AppendLine($"        var result = new byte[Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {stride}u)];");
        source.AppendLine($"        {arrayName}(values, result);");
        source.AppendLine("        return result;");
        source.AppendLine("    }");
        source.AppendLine();
        reason = null;
        return true;
    }

    private static bool TryAppendArrayUnpackMethods(
        StringBuilder source,
        string elementName,
        string arrayName,
        string elementType,
        uint stride,
        out string? reason)
    {
        if (stride > int.MaxValue)
        {
            reason = "The resolved array stride exceeds the managed span index range.";
            return false;
        }

        var strideText = stride.ToString(CultureInfo.InvariantCulture);
        source.AppendLine($"    public static int {arrayName}(ReadOnlySpan<byte> source, Span<{elementType}> values)");
        source.AppendLine("    {");
        source.AppendLine($"        int required = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(values.Length, {stride}u);");
        source.AppendLine("        Delta.Shader.Packing.Std430Packer.RequireCapacity(source, (uint)required);");
        source.AppendLine("        for (int index = 0; index < values.Length; index++)");
        source.AppendLine("        {");
        source.AppendLine($"            values[index] = {elementName}(source.Slice(checked(index * {strideText}), {strideText}));");
        source.AppendLine("        }");
        source.AppendLine("        return required;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    public static {elementType}[] {arrayName}(ReadOnlySpan<byte> source)");
        source.AppendLine("    {");
        source.AppendLine($"        const int stride = {strideText};");
        source.AppendLine("        if (source.Length % stride != 0)");
        source.AppendLine("        {");
        source.AppendLine("            throw new ArgumentException(\"The source length must be a multiple of the resolved std430 array stride.\", nameof(source));");
        source.AppendLine("        }");
        source.AppendLine($"        var result = new {elementType}[source.Length / stride];");
        source.AppendLine($"        {arrayName}(source, result);");
        source.AppendLine("        return result;");
        source.AppendLine("    }");
        source.AppendLine();
        reason = null;
        return true;
    }

    private static void AppendPackMethod(
        StringBuilder methods,
        string name,
        string typeName,
        uint size,
        StringBuilder operations)
    {
        var sizeText = size.ToString(CultureInfo.InvariantCulture);
        methods.AppendLine($"    public static int {name}(in {typeName} value, Span<byte> destination)");
        methods.AppendLine("    {");
        methods.AppendLine($"        Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, {size}u);");
        methods.AppendLine($"        destination.Slice(0, {sizeText}).Clear();");
        methods.AppendLine("        var writer = new Delta.Shader.Packing.Std430Writer(destination);");
        methods.Append(operations);
        methods.AppendLine("        return " + sizeText + ";");
        methods.AppendLine("    }");
        methods.AppendLine();
        methods.AppendLine($"    public static byte[] {name}(in {typeName} value)");
        methods.AppendLine("    {");
        methods.AppendLine($"        var result = new byte[{sizeText}];");
        methods.AppendLine($"        {name}(in value, result);");
        methods.AppendLine("        return result;");
        methods.AppendLine("    }");
        methods.AppendLine();
    }

    private static void AppendUnpackMethod(
        StringBuilder methods,
        string name,
        string typeName,
        uint size,
        StringBuilder operations)
    {
        var sizeText = size.ToString(CultureInfo.InvariantCulture);
        methods.AppendLine($"    public static {typeName} {name}(ReadOnlySpan<byte> source)");
        methods.AppendLine("    {");
        methods.AppendLine($"        Delta.Shader.Packing.Std430Packer.RequireCapacity(source, {size}u);");
        methods.AppendLine($"        {typeName} value = default;");
        methods.AppendLine("        var reader = new Delta.Shader.Packing.Std430Reader(source);");
        methods.Append(operations);
        methods.AppendLine("        return value;");
        methods.AppendLine("    }");
        methods.AppendLine();
    }

    private static string FullyQualifiedType(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string SanitizeIdentifier(string name)
        => string.Concat(name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')) is { Length: > 0 } value
            ? value
            : "Value";

    private static string Resources(IReadOnlyList<ShaderCompilationResource> resources)
        => ArrayExpression(resources, "Delta.Shader.Contract.ShaderResourceBinding", RenderResource);

    private static string PushConstants(ShaderStage stage, IReadOnlyList<ShaderCompilationPushConstant> pushConstants)
        => ArrayExpression(pushConstants, "Delta.Shader.Contract.ShaderPushConstantRange", push => RenderPushConstant(stage, push));

    private static string Interfaces(IReadOnlyList<ShaderCompilationInterfaceVariable> variables)
        => ArrayExpression(variables, "Delta.Shader.Contract.ShaderInterfaceVariable", RenderInterface);

    private static string VertexInputs(IReadOnlyList<ShaderCompilationVertexInput> inputs)
        => ArrayExpression(inputs, "Delta.Shader.Contract.ShaderVertexInput", RenderVertexInput);

    private static string VertexBuffers(IReadOnlyList<ShaderCompilationVertexBufferBinding> buffers)
        => ArrayExpression(buffers, "Delta.Shader.Contract.ShaderVertexBufferLayout", RenderVertexBuffer);

    private static string Workgroup(ShaderCompilationManifest manifest)
        => $"new Delta.Shader.Contract.ShaderWorkgroupSize({manifest.LocalSizeX}u, {manifest.LocalSizeY}u, {manifest.LocalSizeZ}u)";

    private static string RenderResource(ShaderCompilationResource resource)
    {
        var kind = resource.Category switch
        {
            "storage-buffer" => "StorageBuffer",
            "sampled-texture" or "sampled-texture-2d" => "SampledTexture",
            "combined-texture-sampler" => "CombinedTextureSampler",
            _ => "Unknown"
        };
        var layout = kind is "SampledTexture" or "CombinedTextureSampler"
            ? "Delta.Shader.Contract.ShaderAbiLayout.Empty"
            : RenderLayout(resource.Size, resource.Alignment, resource.ArrayStride, resource.MatrixStride ?? 0u, resource.Members);
        var access = resource.Access == 0
            ? (resource.ReadOnly ? "Read" : "ReadWrite")
            : resource.Access.ToString();
        return $"new Delta.Shader.Contract.ShaderResourceBinding(new Delta.Shader.Contract.ShaderBinding({resource.Set}u, {resource.Binding}u), Delta.Shader.Contract.ShaderResourceKind.{kind}, Delta.Shader.Contract.ShaderResourceAccess.{access}, {StageMask(resource.Stage)}, layout: {layout}, descriptorCount: 1u)";
    }

    private static string RenderPushConstant(ShaderStage stage, ShaderCompilationPushConstant pushConstant)
        => $"new Delta.Shader.Contract.ShaderPushConstantRange(0u, {pushConstant.Size}u, {StageMask(stage)}, {RenderLayout(pushConstant.Size, pushConstant.Alignment, pushConstant.ArrayStride, 0u, pushConstant.Members)})";

    private static string RenderInterface(ShaderCompilationInterfaceVariable variable)
    {
        var location = IsBuiltin(variable.Builtin) ? "null" : variable.Location.ToString(CultureInfo.InvariantCulture) + "u";
        return $"new Delta.Shader.Contract.ShaderInterfaceVariable({ValueType(variable.GlslType)}, Location: {location}, Builtin: {Builtin(variable.Builtin)})";
    }

    private static string RenderVertexInput(ShaderCompilationVertexInput input)
        => $"new Delta.Shader.Contract.ShaderVertexInput({input.Location}u, {input.Binding}u, {input.ByteOffset}u, {ValueType(input.GlslType)}, Delta.Shader.Contract.ShaderVertexInputRate.{input.InputRate})";

    private static string RenderVertexBuffer(ShaderCompilationVertexBufferBinding buffer)
        => $"new Delta.Shader.Contract.ShaderVertexBufferLayout({buffer.Binding}u, {buffer.Stride}u, Delta.Shader.Contract.ShaderVertexInputRate.{buffer.InputRate})";

    private static string RenderLayout(uint size, uint alignment, uint arrayStride, uint matrixStride, IReadOnlyList<ShaderCompilationMember> members)
        => $"new Delta.Shader.Contract.ShaderAbiLayout({size}u, {alignment}u, arrayStride: {arrayStride}u, matrixStride: {matrixStride}u, members: {ArrayExpression(members, "Delta.Shader.Contract.ShaderAbiMember", RenderMember)})";

    private static string RenderMember(ShaderCompilationMember member)
    {
        var nested = IsStructure(member.GlslType)
            ? RenderLayout(member.Size, member.Alignment, member.ArrayStride, member.MatrixStride ?? 0u, member.Members)
            : "null";
        return $"new Delta.Shader.Contract.ShaderAbiMember({ValueType(member.GlslType)}, {member.Offset}u, {member.Size}u, {member.Alignment}u, arrayStride: {member.ArrayStride}u, matrixStride: {member.MatrixStride ?? 0u}u, nestedLayout: {nested})";
    }

    private static string ArrayExpression<T>(IEnumerable<T>? values, string typeName, Func<T, string> render)
    {
        var items = values?.ToArray() ?? Array.Empty<T>();
        if (items.Length == 0)
        {
            return $"Array.Empty<{typeName}>()";
        }

        var rendered = items.Select(value => "                " + render(value));
        return $"new {typeName}[]\n            {{\n{string.Join(",\n", rendered)}\n            }}";
    }

    private static string ValueType(string? glslType)
    {
        if (IsStructure(glslType))
        {
            return "Delta.Shader.Contract.ShaderValueType.Structure";
        }

        var type = glslType ?? string.Empty;
        var (kind, bits, vectorSize, columns) = type switch
        {
            "bool" => ("Boolean", 32u, 1u, 1u),
            "int" => ("SignedInteger", 32u, 1u, 1u),
            "uint" => ("UnsignedInteger", 32u, 1u, 1u),
            "float" => ("FloatingPoint", 32u, 1u, 1u),
            "double" => ("FloatingPoint", 64u, 1u, 1u),
            "vec2" or "vec3" or "vec4" => ("FloatingPoint", 32u, VectorSize(type), 1u),
            "ivec2" or "ivec3" or "ivec4" => ("SignedInteger", 32u, VectorSize(type), 1u),
            "uvec2" or "uvec3" or "uvec4" => ("UnsignedInteger", 32u, VectorSize(type), 1u),
            "bvec2" or "bvec3" or "bvec4" => ("Boolean", 32u, VectorSize(type), 1u),
            "dvec2" or "dvec3" or "dvec4" => ("FloatingPoint", 64u, VectorSize(type), 1u),
            "mat2" or "mat3" or "mat4" => ("FloatingPoint", 32u, VectorSize(type), VectorSize(type)),
            _ => ("Unknown", 0u, 0u, 0u)
        };
        return $"new Delta.Shader.Contract.ShaderValueType(Delta.Shader.Contract.ShaderValueKind.{kind}, {bits}u, {vectorSize}u, {columns}u)";
    }

    private static uint VectorSize(string type) => (uint)(type[type.Length - 1] - '0');

    private static string Stage(ShaderStage stage) => $"Delta.Shader.Contract.ShaderStage.{stage}";

    private static string StageMask(ShaderStage stage) => stage switch
    {
        ShaderStage.Compute => "Delta.Shader.Contract.ShaderStageMask.Compute",
        ShaderStage.Vertex => "Delta.Shader.Contract.ShaderStageMask.Vertex",
        ShaderStage.Fragment => "Delta.Shader.Contract.ShaderStageMask.Fragment",
        _ => "Delta.Shader.Contract.ShaderStageMask.None"
    };

    private static string Builtin(string? builtin) => builtin switch
    {
        "FragmentCoord" => "Delta.Shader.Contract.ShaderBuiltin.FragmentCoordinate",
        "Position" => "Delta.Shader.Contract.ShaderBuiltin.Position",
        "VertexIndex" => "Delta.Shader.Contract.ShaderBuiltin.VertexIndex",
        "InstanceIndex" => "Delta.Shader.Contract.ShaderBuiltin.InstanceIndex",
        "FragmentColor" => "Delta.Shader.Contract.ShaderBuiltin.None",
        null or "" => "Delta.Shader.Contract.ShaderBuiltin.None",
        _ => "Delta.Shader.Contract.ShaderBuiltin.Unknown"
    };

    private static bool IsBuiltin(string? builtin) => !string.IsNullOrWhiteSpace(builtin) && builtin != "FragmentColor";

    private static bool IsStructure(string? glslType)
        => glslType is not null && glslType.Length > 0 && glslType.StartsWith("DeltaStruct_", StringComparison.Ordinal);
}
