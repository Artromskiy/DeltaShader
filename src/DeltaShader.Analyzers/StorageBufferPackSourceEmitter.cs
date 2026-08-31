using System.Collections.Generic;
using System.Text;
using Delta.Shader.Compiler;

namespace Delta.Shader.Analyzers;

internal static partial class ArtifactSourceEmitter
{
    private static bool TryAppendStorageBufferRangeMethods(
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

        source.AppendLine($$"""
                public const int {{stem}}StorageBufferCount = {{resources.Count}};

                public static int Get{{stem}}StorageBufferByteLength(int elementCount)
                {
                    Span<Delta.Shader.Packing.ShaderBufferRange> ranges = stackalloc Delta.Shader.Packing.ShaderBufferRange[{{resources.Count}}];
                    return Get{{stem}}StorageBufferRanges(elementCount, ranges);
                }

                public static int Get{{stem}}StorageBufferRanges(
                    int elementCount,
                    Span<Delta.Shader.Packing.ShaderBufferRange> destination)
                {
                    Delta.Shader.Packing.Std430Packer.RequireCapacity(destination, {{resources.Count}});
                    var offset = 0u;
            {{rangeAssignments.ToString().TrimEnd()}}
                    return checked((int)Delta.Shader.Packing.Std430Packer.AlignOffset(offset, 16u));
                }
            """);
        return true;
    }
}
