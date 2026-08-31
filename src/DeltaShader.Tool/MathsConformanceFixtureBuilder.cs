using System.Globalization;
using System.Text;

namespace Delta.Shader.Tool;

internal static class MathsConformanceFixtureBuilder
{
    public static string Build(IReadOnlyList<ContractFunction> functions)
    {
        var builder = new StringBuilder(
            """
            using Delta.Maths;
            using Delta.Shader;

            namespace Delta.Shader.MathsConformance.Generated;

            public static class MathsConformanceFixtures
            {
            """);
        for (var index = 0; index < functions.Count; index++)
        {
            Append(builder, functions[index], index);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, ContractFunction function, int index)
    {
        var methodName = $"Case{index:0000}";
        var contextName = methodName + "Context";
        if (function.ParameterModifiers.Length != function.ParameterTypes.Length)
        {
            throw new InvalidOperationException(
                $"Manifest parameter modifier count does not match parameter count for {function.Identity}.");
        }

        var inputSlotByParameter = new int[function.ParameterTypes.Length];
        var outSlotByParameter = new int[function.ParameterTypes.Length];
        Array.Fill(inputSlotByParameter, -1);
        Array.Fill(outSlotByParameter, -1);
        var inputCount = 0;
        var outCount = 0;
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var modifier = function.ParameterModifiers[parameterIndex];
            if (modifier is "none" or "ref")
            {
                inputSlotByParameter[parameterIndex] = inputCount++;
            }
            else if (modifier == "out")
            {
                outSlotByParameter[parameterIndex] = outCount++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported parameter modifier '{modifier}' in {function.Identity}.");
            }
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"    public readonly struct {contextName}");
        builder.AppendLine("    {");
        var binding = 0;
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var inputSlot = inputSlotByParameter[parameterIndex];
            if (inputSlot < 0)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadOnlyStorageBuffer<{function.ParameterTypes[parameterIndex]}> Input{inputSlot};");
            binding++;
        }

        if (function.ReturnType != "void")
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadWriteStorageBuffer<{function.ReturnType}> Output;");
            binding++;
        }

        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var outSlot = outSlotByParameter[parameterIndex];
            if (outSlot < 0)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"        [Layout(0, {binding})]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"        public readonly ReadWriteStorageBuffer<{function.ParameterTypes[parameterIndex]}> Out{outSlot};");
            binding++;
        }

        builder.AppendLine();
        builder.AppendLine("        [PushConstant]");
        builder.AppendLine("        public readonly uint Count;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    [ComputeShader(localSizeX: 64)]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public static void {methodName}(in {contextName} context)");
        builder.AppendLine("    {");
        builder.AppendLine("        uint index = ShaderBuiltins.GlobalInvocationId.X;");
        builder.AppendLine(inputCount == 0
            ? "        if (index >= context.Count)"
            : "        if (index >= context.Count || index >= context.Input0.Length)");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        var arguments = new string[function.ParameterTypes.Length];
        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var modifier = function.ParameterModifiers[parameterIndex];
            if (modifier == "out")
            {
                arguments[parameterIndex] = $"out out{outSlotByParameter[parameterIndex]}";
                builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ParameterTypes[parameterIndex]} out{outSlotByParameter[parameterIndex]};");
            }
            else
            {
                var inputSlot = inputSlotByParameter[parameterIndex];
                var inputExpression = $"context.Input{inputSlot}[index]";
                if (modifier == "ref")
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ParameterTypes[parameterIndex]} ref{inputSlot} = {inputExpression};");
                    arguments[parameterIndex] = $"ref ref{inputSlot}";
                }
                else
                {
                    arguments[parameterIndex] = inputExpression;
                }
            }
        }

        var operatorToken = GetOperatorToken(function);
        string expression;
        if (operatorToken is null)
        {
            expression = $"Delta.Maths.{function.OwnerType}.{function.MethodName}({string.Join(", ", arguments)})";
        }
        else if (arguments.Length == 1)
        {
            expression = $"({operatorToken}{arguments[0]})";
        }
        else if (arguments.Length == 2)
        {
            expression = $"({arguments[0]} {operatorToken} {arguments[1]})";
        }
        else
        {
            throw new InvalidOperationException(
                $"Operator {function.Identity} has {arguments.Length} operands.");
        }

        if (function.ReturnType == "void")
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        {expression};");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        {function.ReturnType} result = {expression};");
            builder.AppendLine("        context.Output[index] = result;");
        }

        for (var parameterIndex = 0; parameterIndex < function.ParameterTypes.Length; parameterIndex++)
        {
            var outSlot = outSlotByParameter[parameterIndex];
            if (outSlot >= 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"        context.Out{outSlot}[index] = out{outSlot};");
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string? GetOperatorToken(ContractFunction function)
    {
        if (!function.MethodName.StartsWith("op_", StringComparison.Ordinal))
        {
            return null;
        }

        if (function.Mapping == "Builtin" && function.GlslName.Length != 0)
        {
            return function.GlslName;
        }

        return function.MethodName switch
        {
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_BitwiseAnd" => "&",
            "op_BitwiseOr" => "|",
            "op_ExclusiveOr" => "^",
            "op_LeftShift" => "<<",
            "op_RightShift" => ">>",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_UnaryNegation" => "-",
            "op_UnaryPlus" => "+",
            "op_OnesComplement" => "~",
            _ => null
        };
    }
}
