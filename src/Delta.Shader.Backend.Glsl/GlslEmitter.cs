using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using System.Text.RegularExpressions;

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
        sb.AppendLine($"layout(local_size_x = {module.LocalSizeX}, local_size_y = {module.LocalSizeY}, local_size_z = {module.LocalSizeZ}) in;");
        var identifiers = new GlslIdentifierMangler("main");

        foreach (var structure in OrderStructs(module.Structs))
        {
            sb.AppendLine($"struct {structure.GlslName}");
            sb.AppendLine("{");
            foreach (var member in structure.Members)
            {
                sb.AppendLine($"    {member.GlslType} {member.GlslName};");
            }

            sb.AppendLine("};");
            sb.AppendLine();
        }

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

            if (!string.IsNullOrWhiteSpace(resource.Name))
            {
                identifierMap[resource.Name] = instanceName;
                identifierMap[$"{resource.Name}.data"] = $"{instanceName}.{dataMemberName}";
            }
        }

        if (module.UsesBuiltinInvocationId && !string.IsNullOrWhiteSpace(module.InvocationParameterName))
        {
            identifierMap[module.InvocationParameterName] = identifiers.Mangle(module.InvocationParameterName, "invocationIndex");
        }

        sb.AppendLine();
        sb.AppendLine("void main()");
        sb.AppendLine("{");

        if (module.UsesBuiltinInvocationId && !string.IsNullOrWhiteSpace(module.InvocationParameterName))
        {
            var invocationName = identifierMap.TryGetValue(module.InvocationParameterName, out var mappedInvocation)
                ? mappedInvocation
                : module.InvocationParameterName;

            sb.AppendLine($"    uint {invocationName} = gl_GlobalInvocationID.x;");
        }

        if (!string.IsNullOrWhiteSpace(module.Body))
        {
            var bodySource = NormalizeBody(module.Body);
            if (identifierMap.Count > 0)
            {
                bodySource = RewriteIdentifiers(bodySource, identifierMap);
            }

            var indentedBody = bodySource.Replace("\n", "\n    ", StringComparison.Ordinal);
            sb.AppendLine("    " + indentedBody);
            sb.AppendLine();
        }

        if (string.IsNullOrWhiteSpace(module.Body))
        {
            sb.AppendLine("    // Delta.Shader auto-generated stage stub");
        }
        sb.AppendLine("}");

        return new GlslEmitResult { Source = sb.ToString() };
    }

    private static string NormalizeBody(string body)
    {
        return body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static string RewriteIdentifiers(string body, Dictionary<string, string> identifierMap)
    {
        if (identifierMap.Count == 0)
        {
            return body;
        }

        var rewritten = body;
        foreach (var entry in identifierMap.OrderByDescending(e => e.Key.Length))
        {
            if (entry.Key.EndsWith(".data", StringComparison.Ordinal))
            {
                rewritten = rewritten.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
                continue;
            }

            var pattern = $"\\b{Regex.Escape(entry.Key)}\\b";
            rewritten = Regex.Replace(rewritten, pattern, entry.Value, RegexOptions.None);
        }

        return rewritten;
    }

    private static IReadOnlyList<ShaderIrStruct> OrderStructs(IReadOnlyList<ShaderIrStruct> structures)
    {
        var byGlslName = structures.ToDictionary(structure => structure.GlslName, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ShaderIrStruct>(structures.Count);

        void Visit(ShaderIrStruct structure)
        {
            if (visited.Contains(structure.GlslName))
            {
                return;
            }

            if (!active.Add(structure.GlslName))
            {
                throw new InvalidOperationException($"Recursive GLSL struct dependency '{structure.GlslName}'.");
            }

            foreach (var member in structure.Members)
            {
                if (member.Members.Count > 0 && byGlslName.TryGetValue(member.GlslType, out var dependency))
                {
                    Visit(dependency);
                }
            }

            active.Remove(structure.GlslName);
            visited.Add(structure.GlslName);
            ordered.Add(structure);
        }

        foreach (var structure in structures.OrderBy(structure => structure.GlslName, StringComparer.Ordinal))
        {
            Visit(structure);
        }

        return ordered;
    }

}
