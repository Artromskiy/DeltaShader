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
        if (!TryAppendStorageAssignments(rangeAssignments, resources, 0, out reason))
        {
            return false;
        }

        AppendRangeMethods(
            source,
            stem + "StorageBuffer",
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
        if (!TryAppendVertexAssignments(rangeAssignments, bindings, 0, out reason))
        {
            return false;
        }

        AppendRangeMethods(
            source,
            stem + "VertexBuffer",
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

    public static bool TryAppendSharedBufferRangeMethods(
        StringBuilder source,
        string stem,
        IReadOnlyList<ShaderCompilationResource> resources,
        IReadOnlyList<ShaderCompilationVertexBufferBinding> bindings,
        out string? reason)
    {
        reason = null;
        var count = checked(resources.Count + bindings.Count);
        if (count == 0)
        {
            return true;
        }

        var rangeAssignments = new StringBuilder();
        if (!TryAppendStorageAssignments(rangeAssignments, resources, 0, out reason) ||
            !TryAppendVertexAssignments(rangeAssignments, bindings, resources.Count, out reason))
        {
            return false;
        }

        AppendRangeMethods(
            source,
            stem + "SharedBuffer",
            count,
            $$"""
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, {{count}});
                """,
            rangeAssignments.ToString().TrimEnd(),
            "                    return checked((int)Delta.Shader.Packing.Std430Packer.AlignOffset(offset, 16u));");
        return true;
    }

    private static bool TryAppendStorageAssignments(
        StringBuilder source,
        IReadOnlyList<ShaderCompilationResource> resources,
        int destinationOffset,
        out string? reason)
    {
        reason = null;
        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];
            if (resource.ArrayStride == 0)
            {
                reason = $"Storage-buffer '{resource.Name}' has no resolved std430 element stride.";
                return false;
            }

            var alignment = resource.Alignment < 16u ? 16u : resource.Alignment;
            var destinationIndex = checked(destinationOffset + index);
            source.AppendLine($$"""
                    offset = Delta.Shader.Packing.Std430Packer.AlignOffset(offset, {{alignment}}u);
                    var size{{destinationIndex}} = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(elementCount, {{resource.ArrayStride}}u);
                    destination[{{destinationIndex}}] = new Delta.Shader.Packing.ShaderBufferRange(
                        {{resource.Set}}u,
                        {{resource.Binding}}u,
                        offset,
                        checked((uint)size{{destinationIndex}}),
                        {{resource.ArrayStride}}u);
                    offset = checked(offset + (uint)size{{destinationIndex}});
                """);
        }

        return true;
    }

    private static bool TryAppendVertexAssignments(
        StringBuilder source,
        IReadOnlyList<ShaderCompilationVertexBufferBinding> bindings,
        int destinationOffset,
        out string? reason)
    {
        reason = null;
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.Stride == 0)
            {
                reason = $"Vertex binding {binding.Binding} has no resolved stride.";
                return false;
            }

            var destinationIndex = checked(destinationOffset + index);
            source.AppendLine($$"""
                    offset = checked((offset + 3u) & ~3u);
                    var size{{destinationIndex}} = Delta.Shader.Packing.Std430Packer.GetArrayByteLength(elementCount, {{binding.Stride}}u);
                    destination[{{destinationIndex}}] = new Delta.Shader.Packing.ShaderBufferRange(
                        0u,
                        {{binding.Binding}}u,
                        offset,
                        checked((uint)size{{destinationIndex}}),
                        {{binding.Stride}}u);
                    offset = checked(offset + (uint)size{{destinationIndex}});
                """);
        }

        return true;
    }

    private static void AppendRangeMethods(
        StringBuilder source,
        string name,
        int count,
        string capacityValidation,
        string rangeAssignments,
        string finalLengthExpression)
    {
        source.AppendLine($$"""
                public const int {{name}}Count = {{count}};

                public static int Get{{name}}ByteLength(int elementCount)
                {
                    Span<Delta.Shader.Packing.ShaderBufferRange> ranges = stackalloc Delta.Shader.Packing.ShaderBufferRange[{{count}}];
                    return Get{{name}}Ranges(elementCount, ranges);
                }

                public static int Get{{name}}Ranges(
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
