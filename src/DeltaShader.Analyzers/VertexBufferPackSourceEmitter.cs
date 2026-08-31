using System.Collections.Generic;
using System.Text;
using Delta.Shader.Compiler;

namespace Delta.Shader.Analyzers;

internal static partial class ArtifactSourceEmitter
{
    private static bool TryAppendVertexBufferRangeMethods(
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

        source.AppendLine($$"""
                public const int {{stem}}VertexBufferCount = {{bindings.Count}};

                public static int Get{{stem}}VertexBufferByteLength(int elementCount)
                {
                    Span<Delta.Shader.Packing.ShaderBufferRange> ranges = stackalloc Delta.Shader.Packing.ShaderBufferRange[{{bindings.Count}}];
                    return Get{{stem}}VertexBufferRanges(elementCount, ranges);
                }

                public static int Get{{stem}}VertexBufferRanges(
                    int elementCount,
                    Span<Delta.Shader.Packing.ShaderBufferRange> destination)
                {
                    if (destination.Length < {{bindings.Count}})
                    {
                        throw new ArgumentException(
                            "The destination must contain one range per vertex binding.",
                            nameof(destination));
                    }

                    var offset = 0u;
            {{rangeAssignments.ToString().TrimEnd()}}
                    return checked((int)((offset + 3u) & ~3u));
                }
            """);
        return true;
    }
}
