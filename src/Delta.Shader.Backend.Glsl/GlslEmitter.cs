using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Backend.Glsl;

public sealed class GlslEmitResult
{
    public string Source { get; init; } = string.Empty;
    public bool Success => !string.IsNullOrWhiteSpace(Source);
}

public static class GlslEmitter
{
    public static GlslEmitResult EmitFromModule(ShaderIrModule module)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#version 460");
        if (module.Stage == ShaderStage.Compute)
            sb.AppendLine($"layout(local_size_x = {module.LocalSizeX}, local_size_y = {module.LocalSizeY}, local_size_z = {module.LocalSizeZ}) in;");

        var identifiers = new GlslIdentifierMangler("main");
        EmitStructs(sb, module);
        EmitPushConstants(sb, module);
        var identifierMap = EmitResources(sb, module, identifiers);
        EmitInterfaces(sb, module);
        sb.AppendLine();
        sb.AppendLine("void main()");
        sb.AppendLine("{");
        if (module.Stage == ShaderStage.Compute && module.UsesBuiltinInvocationId && !string.IsNullOrWhiteSpace(module.InvocationParameterName))
        {
            var invocationName = identifiers.Mangle(module.InvocationParameterName, "invocationIndex");
            identifierMap[module.InvocationParameterName!] = invocationName;
            sb.AppendLine($"    uint {invocationName} = gl_GlobalInvocationID.x;");
        }

        var body = NormalizeBody(module.Body);
        if (identifierMap.Count > 0) body = RewriteIdentifiers(body, identifierMap);
        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine("    " + body.Replace("\n", "\n    "));
            sb.AppendLine();
        }
        else sb.AppendLine("    // Delta.Shader auto-generated stage stub");
        sb.AppendLine("}");
        return new GlslEmitResult { Source = sb.ToString() };
    }

    private static void EmitStructs(StringBuilder sb, ShaderIrModule module)
    {
        foreach (var structure in OrderStructs(module.Structs))
        {
            sb.AppendLine($"struct {structure.GlslName}");
            sb.AppendLine("{");
            foreach (var member in structure.Members) sb.AppendLine($"    {member.GlslType} {member.GlslName};");
            sb.AppendLine("};");
            sb.AppendLine();
        }
    }

    private static void EmitPushConstants(StringBuilder sb, ShaderIrModule module)
    {
        foreach (var push in module.PushConstants)
        {
            sb.AppendLine("layout(push_constant, std430) uniform DeltaPushConstants");
            sb.AppendLine("{");
            foreach (var member in push.Members)
                sb.AppendLine($"    layout(offset = {member.Offset}) {member.GlslType} {member.GlslName};");
            sb.AppendLine("} pushConstants;");
            sb.AppendLine();
        }
    }

    private static Dictionary<string, string> EmitResources(StringBuilder sb, ShaderIrModule module, GlslIdentifierMangler identifiers)
    {
        var identifierMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in module.Resources)
        {
            var storageMode = resource.ReadOnly ? "readonly " : string.Empty;
            var glslType = string.IsNullOrWhiteSpace(resource.GlslType) ? "uint" : resource.GlslType!;
            var blockName = identifiers.Mangle(resource.Name, "resource");
            var instanceName = identifiers.Mangle(resource.Name + "_instance", "resourceInstance");
            var dataMemberName = identifiers.Mangle("data");
            sb.AppendLine($"layout(set = {resource.Set}, binding = {resource.Binding}, {ShaderStd430Layout.Standard}) {storageMode}buffer {blockName}");
            sb.AppendLine("{");
            sb.AppendLine($"    {glslType} {dataMemberName}[];");
            sb.AppendLine($"}} {instanceName};");
            sb.AppendLine();
            identifierMap[resource.Name] = instanceName;
            identifierMap[$"{resource.Name}.data"] = $"{instanceName}.{dataMemberName}";
        }
        return identifierMap;
    }

    private static void EmitInterfaces(StringBuilder sb, ShaderIrModule module)
    {
        foreach (var variable in module.Inputs.Where(variable => variable.Builtin is null))
            sb.AppendLine($"layout(location = {variable.Location}) in {variable.GlslType} {variable.GlslName};");
        foreach (var variable in module.Outputs.Where(variable => variable.Builtin is null))
            sb.AppendLine($"layout(location = {variable.Location}) out {variable.GlslType} {variable.GlslName};");
        if (module.Outputs.Any(variable => variable.Builtin == "FragmentColor")) sb.AppendLine("layout(location = 0) out vec4 fragColor;");
        if (module.Outputs.Count > 0 || module.Inputs.Count > 0) sb.AppendLine();
    }

    private static string NormalizeBody(string? body)
    {
        var normalized = (body ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (normalized.StartsWith("{", StringComparison.Ordinal) && normalized.EndsWith("}", StringComparison.Ordinal)) normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        return normalized;
    }

    private static string RewriteIdentifiers(string body, Dictionary<string, string> identifierMap)
    {
        var rewritten = body;
        foreach (var entry in identifierMap.OrderByDescending(entry => entry.Key.Length))
        {
            if (entry.Key.EndsWith(".data", StringComparison.Ordinal)) rewritten = rewritten.Replace(entry.Key, entry.Value);
            else rewritten = Regex.Replace(rewritten, $"\\b{Regex.Escape(entry.Key)}\\b", entry.Value, RegexOptions.None);
        }
        return rewritten;
    }

    private static IReadOnlyList<ShaderIrStruct> OrderStructs(IReadOnlyList<ShaderIrStruct> structures)
    {
        var byName = structures.ToDictionary(structure => structure.GlslName, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ShaderIrStruct>(structures.Count);
        void Visit(ShaderIrStruct structure)
        {
            if (visited.Contains(structure.GlslName)) return;
            if (!active.Add(structure.GlslName)) throw new InvalidOperationException($"Recursive GLSL struct dependency '{structure.GlslName}'.");
            foreach (var member in structure.Members)
                if (member.Members.Count > 0 && byName.TryGetValue(member.GlslType, out var dependency)) Visit(dependency);
            active.Remove(structure.GlslName); visited.Add(structure.GlslName); ordered.Add(structure);
        }
        foreach (var structure in structures.OrderBy(structure => structure.GlslName, StringComparer.Ordinal)) Visit(structure);
        return ordered;
    }
}
