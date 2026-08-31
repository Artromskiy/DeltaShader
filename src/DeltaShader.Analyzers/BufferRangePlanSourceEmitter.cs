using System.Text;
using Delta.Shader.Compiler;

namespace Delta.Shader.Analyzers;

internal static class BufferRangePlanSourceEmitter
{
    public static bool TryAppendStorageBufferRangeMethods(
        StringBuilder source,
        string stem,
        IReadOnlyList<ShaderCompilationResource> resources,
        out string? reason)
    {
        reason = null;
        if (resources.Count == 0)
        {
            return true;
        }

        var rangeAssignments = new StringBuilder();
        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];
            if (resource.ArrayStride == 0)
            {
                reason = $"Storage-buffer '{resource.Name}' has no resolved std430 element stride.";
                return false;
            }

            var alignment = resource.Alignment < 16u ? 16u : resource.Alignment;
            rangeAssignments.AppendLine($$"""
                    offset = Delta.Shader.Packing.Std430Packer.AlignOffset(offset, {{alignment}}u);
                    var size{{index}} = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(elementCount, {{resource.ArrayStride}}u);
                    destination[{{index}}] = new Delta.Shader.Packing.ShaderBufferRange(
                        {{resource.Set}}u,
                        {{resource.Binding}}u,
                        offset,
                        checked((uint)size{{index}}),
                        {{resource.ArrayStride}}u);
                    offset = checked(offset + (uint)size{{index}});
                """);
        }

        AppendRangeMethods(
            source,
            stem,
            "StorageBuffer",
            resources.Count,
            $$"""
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, {{resources.Count}});
                """,
            rangeAssignments.ToString().TrimEnd(),
            "                    return checked((int)Delta.Shader.Packing.Std430Packer.AlignOffset(offset, 16u));");
        return true;
    }

    public static bool TryAppendVertexBufferRangeMethods(
        StringBuilder source,
        string stem,
        IReadOnlyList<ShaderCompilationVertexBufferBinding> bindings,
        out string? reason)
    {
        reason = null;
        if (bindings.Count == 0)
        {
            return true;
        }

        var rangeAssignments = new StringBuilder();
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.Stride == 0)
            {
                reason = $"Vertex binding {binding.Binding} has no resolved stride.";
                return false;
            }

            rangeAssignments.AppendLine($$"""
                    offset = checked((offset + 3u) & ~3u);
                    var size{{index}} = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(elementCount, {{binding.Stride}}u);
                    destination[{{index}}] = new Delta.Shader.Packing.ShaderBufferRange(
                        0u,
                        {{binding.Binding}}u,
                        offset,
                        checked((uint)size{{index}}),
                        {{binding.Stride}}u);
                    offset = checked(offset + (uint)size{{index}});
                """);
        }

        AppendRangeMethods(
            source,
            stem,
            "VertexBuffer",
            bindings.Count,
            $$"""
                    if (destination.Length < {{bindings.Count}})
                    {
                        throw new ArgumentException(
                            "The destination must contain one range per vertex binding.",
                            nameof(destination));
                    }
                """,
            rangeAssignments.ToString().TrimEnd(),
            "                    return checked((int)((offset + 3u) & ~3u));");
        return true;
    }

    private static void AppendRangeMethods(
        StringBuilder source,
        string stem,
        string kind,
        int count,
        string capacityValidation,
        string rangeAssignments,
        string finalLengthExpression)
    {
        source.AppendLine($$"""
                public const int {{stem}}{{kind}}Count = {{count}};

                public static int Get{{stem}}{{kind}}ByteLength(int elementCount)
                {
                    Span<Delta.Shader.Packing.ShaderBufferRange> ranges = stackalloc Delta.Shader.Packing.ShaderBufferRange[{{count}}];
                    return Get{{stem}}{{kind}}Ranges(elementCount, ranges);
                }

                public static int Get{{stem}}{{kind}}Ranges(
                    int elementCount,
                    Span<Delta.Shader.Packing.ShaderBufferRange> destination)
                {
{{capacityValidation}}
                    var offset = 0u;
{{rangeAssignments}}
{{finalLengthExpression}}
                }
            """);
    }
}
