using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler;

internal static class GraphicsInterstageResolver
{
    public static IReadOnlyList<ShaderCompilationResult> ResolvePairs(
        IReadOnlyList<ShaderCompilationResult> results,
        ShaderCompilationOptions options)
    {
        var resolved = results.ToArray();
        var vertices = results
            .Select((result, index) => (result, index))
            .Where(item => item.result.Success && item.result.Module?.Stage == ShaderStage.Vertex)
            .ToArray();
        var fragments = results
            .Select((result, index) => (result, index))
            .Where(item => item.result.Success && item.result.Module?.Stage == ShaderStage.Fragment)
            .ToArray();
        var usedFragments = new HashSet<int>();

        if (vertices.Length == 1 && fragments.Length == 1)
        {
            ResolvePair(vertices[0], fragments[0], resolved, options);
            return resolved;
        }

        foreach (var vertex in vertices)
        {
            var matches = fragments
                .Where(fragment => !usedFragments.Contains(fragment.index) &&
                    string.Equals(
                        vertex.result.Module!.SourceEntryPointName,
                        fragment.result.Module!.SourceEntryPointName,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                continue;
            }

            usedFragments.Add(matches[0].index);
            ResolvePair(vertex, matches[0], resolved, options);
        }

        return resolved;
    }

    private static void ResolvePair(
        (ShaderCompilationResult result, int index) vertex,
        (ShaderCompilationResult result, int index) fragment,
        ShaderCompilationResult[] results,
        ShaderCompilationOptions options)
    {
        var vertexModule = vertex.result.Module;
        var fragmentModule = fragment.result.Module;
        if (vertexModule is null || fragmentModule is null)
        {
            return;
        }

        var vertexVariables = vertexModule.Outputs.Where(variable => variable.Builtin is null).ToArray();
        var fragmentVariables = fragmentModule.Inputs.Where(variable => variable.Builtin is null).ToArray();
        if (vertexVariables.Length == 0 || vertexVariables.Length != fragmentVariables.Length)
        {
            return;
        }

        var pairs = new List<InterstagePair>(vertexVariables.Length);
        for (var index = 0; index < vertexVariables.Length; index++)
        {
            var vertexVariable = vertexVariables[index];
            var fragmentVariable = fragmentVariables[index];
            if (!string.Equals(vertexVariable.GlslType, fragmentVariable.GlslType, StringComparison.Ordinal))
            {
                return;
            }

            TryGetShape(vertexVariable.GlslType, out var scalarType, out var componentCount);
            pairs.Add(new InterstagePair
            {
                Vertex = vertexVariable,
                Fragment = fragmentVariable,
                ScalarType = scalarType,
                ComponentCount = componentCount
            });
        }

        var slots = CreateSlots(pairs);
        if (slots.Count == pairs.Count)
        {
            return;
        }

        var vertexBody = RewriteBody(vertexModule.Body, pairs, stage: ShaderStage.Vertex);
        var fragmentBody = RewriteBody(fragmentModule.Body, pairs, stage: ShaderStage.Fragment);
        var resolvedVertex = CloneModule(
            vertexModule,
            vertexBody,
            inputs: vertexModule.Inputs,
            outputs: ReplaceOutputs(vertexModule.Outputs, slots),
            contextFields: ReplaceContextFields(vertexModule, pairs, ShaderStage.Vertex));
        var resolvedFragment = CloneModule(
            fragmentModule,
            fragmentBody,
            inputs: ReplaceInputs(fragmentModule.Inputs, slots),
            outputs: fragmentModule.Outputs,
            contextFields: ReplaceContextFields(fragmentModule, pairs, ShaderStage.Fragment));

        results[vertex.index] = CloneResult(vertex.result, resolvedVertex, options);
        results[fragment.index] = CloneResult(fragment.result, resolvedFragment, options);
    }

    private static IReadOnlyList<InterstageSlot> CreateSlots(IReadOnlyList<InterstagePair> pairs)
    {
        var slots = new List<InterstageSlot>();
        foreach (var pair in pairs)
        {
            if (!TryGetShape(pair.Vertex.GlslType, out var scalarType, out var componentCount))
            {
                var unsupportedSlot = new InterstageSlot { ScalarType = string.Empty, ComponentCount = 4 };
                unsupportedSlot.Members.Add(pair);
                pair.Slot = unsupportedSlot;
                slots.Add(unsupportedSlot);
                continue;
            }

            var slot = slots.FirstOrDefault(candidate =>
                candidate.ComponentCount < 4 &&
                string.Equals(candidate.ScalarType, scalarType, StringComparison.Ordinal) &&
                candidate.ComponentCount + componentCount <= 4);

            if (slot is null)
            {
                slot = new InterstageSlot { ScalarType = scalarType };
                slots.Add(slot);
            }

            pair.ComponentOffset = slot.ComponentCount;
            pair.Slot = slot;
            slot.ComponentCount += componentCount;
            slot.Members.Add(pair);
        }

        for (var index = 0; index < slots.Count; index++)
        {
            slots[index].Location = (uint)index;
            slots[index].PhysicalName = "interstage_slot_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            slots[index].PhysicalType = slots[index].Members.Count > 1
                ? CreateVectorType(slots[index].ScalarType)
                : slots[index].Members[0].Vertex.GlslType;
        }

        return slots;
    }

    private static IReadOnlyList<ShaderIrInterfaceVariable> ReplaceOutputs(
        IReadOnlyList<ShaderIrInterfaceVariable> original,
        IReadOnlyList<InterstageSlot> slots)
        => original.Where(variable => variable.Builtin is not null)
            .Concat(slots.Select(slot => CreateInterfaceVariable(slot, ShaderStage.Vertex)))
            .ToArray();

    private static IReadOnlyList<ShaderIrInterfaceVariable> ReplaceInputs(
        IReadOnlyList<ShaderIrInterfaceVariable> original,
        IReadOnlyList<InterstageSlot> slots)
        => original.Where(variable => variable.Builtin is not null)
            .Concat(slots.Select(slot => CreateInterfaceVariable(slot, ShaderStage.Fragment)))
            .ToArray();

    private static ShaderIrInterfaceVariable CreateInterfaceVariable(InterstageSlot slot, ShaderStage stage)
    {
        var pair = slot.Members[0];
        var source = stage == ShaderStage.Vertex ? pair.Vertex : pair.Fragment;
        return new ShaderIrInterfaceVariable
        {
            Name = source.Name,
            ParameterName = source.ParameterName,
            GlslName = slot.PhysicalName,
            GlslType = slot.PhysicalType,
            Location = slot.Location
        };
    }

    private static string? RewriteBody(
        string? body,
        IReadOnlyList<InterstagePair> pairs,
        ShaderStage stage)
    {
        if (body is null)
        {
            return null;
        }

        var rewritten = body;
        foreach (var pair in pairs)
        {
            var source = stage == ShaderStage.Vertex ? pair.Vertex : pair.Fragment;
            var replacement = pair.Slot!.PhysicalName + CreateSwizzle(pair.ComponentOffset, pair.ComponentCount);
            rewritten = Regex.Replace(
                rewritten,
                $"(?<![A-Za-z0-9_]){Regex.Escape(source.GlslName)}(?![A-Za-z0-9_])",
                replacement,
                RegexOptions.None);
        }

        return rewritten;
    }

    private static ShaderIrModule CloneModule(
        ShaderIrModule module,
        string? body,
        IReadOnlyList<ShaderIrInterfaceVariable> inputs,
        IReadOnlyList<ShaderIrInterfaceVariable> outputs,
        IReadOnlyList<ShaderIrContextField> contextFields)
        => new()
        {
            Stage = module.Stage,
            SourceEntryPointName = module.SourceEntryPointName,
            EntryPointName = module.EntryPointName,
            LocalSizeX = module.LocalSizeX,
            LocalSizeY = module.LocalSizeY,
            LocalSizeZ = module.LocalSizeZ,
            Resources = module.Resources,
            Structs = module.Structs,
            Requirements = module.Requirements,
            Instructions = module.Instructions,
            Body = body,
            HelperFunctions = module.HelperFunctions,
            UsesBuiltinInvocationId = module.UsesBuiltinInvocationId,
            InvocationParameterName = module.InvocationParameterName,
            Inputs = inputs,
            VertexInputs = module.VertexInputs,
            VertexBuffers = module.VertexBuffers,
            Outputs = outputs,
            PushConstants = module.PushConstants,
            ContextFields = contextFields
        };

    private static IReadOnlyList<ShaderIrContextField> ReplaceContextFields(
        ShaderIrModule module,
        IReadOnlyList<InterstagePair> pairs,
        ShaderStage stage)
        => module.ContextFields.Select(field =>
        {
            var pair = pairs.FirstOrDefault(candidate =>
                string.Equals(
                    stage == ShaderStage.Vertex ? candidate.Vertex.Name : candidate.Fragment.Name,
                    field.SourcePath,
                    StringComparison.Ordinal));
            if (pair?.Slot is null)
            {
                return field;
            }

            var source = stage == ShaderStage.Vertex ? pair.Vertex : pair.Fragment;
            var replacement = pair.Slot.PhysicalName + CreateSwizzle(pair.ComponentOffset, pair.ComponentCount);
            return new ShaderIrContextField
            {
                TypeIdentity = field.TypeIdentity,
                SourcePath = field.SourcePath,
                ReadGlslName = stage == ShaderStage.Fragment && field.ReadGlslName == source.GlslName
                    ? replacement
                    : field.ReadGlslName,
                WriteGlslName = stage == ShaderStage.Vertex && field.WriteGlslName == source.GlslName
                    ? replacement
                    : field.WriteGlslName,
                GlslType = field.GlslType,
                Kind = field.Kind,
                Stage = field.Stage,
                HostProvided = field.HostProvided,
                Location = field.Location,
                ResourceKind = field.ResourceKind,
                Access = field.Access,
                Set = field.Set,
                Binding = field.Binding,
                Alignment = field.Alignment,
                Size = field.Size,
                ArrayStride = field.ArrayStride
            };
        }).ToArray();

    private static ShaderCompilationResult CloneResult(
        ShaderCompilationResult result,
        ShaderIrModule module,
        ShaderCompilationOptions options)
        => new(
            result.EntryPointName,
            result.Success,
            result.Diagnostics,
            module,
            options,
            result.SourceMethodName,
            result.SourceMethodIdentity);

    private static bool TryGetShape(string glslType, out string scalarType, out uint componentCount)
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

    private static string CreateVectorType(string scalarType)
        => scalarType switch
        {
            "float" => "vec4",
            "int" => "ivec4",
            "uint" => "uvec4",
            _ => throw new ArgumentException($"Unsupported interstage scalar type '{scalarType}'.", nameof(scalarType))
        };

    private static string CreateSwizzle(uint componentOffset, uint componentCount)
    {
        const string components = "xyzw";
        return componentOffset == 0 && componentCount == 4
            ? string.Empty
            : "." + components.Substring((int)componentOffset, (int)componentCount);
    }

    private sealed class InterstagePair
    {
        public ShaderIrInterfaceVariable Vertex { get; init; } = null!;
        public ShaderIrInterfaceVariable Fragment { get; init; } = null!;
        public string ScalarType { get; init; } = string.Empty;
        public uint ComponentCount { get; init; }
        public uint ComponentOffset { get; set; }
        public InterstageSlot? Slot { get; set; }
    }

    private sealed class InterstageSlot
    {
        public string ScalarType { get; init; } = string.Empty;
        public uint ComponentCount { get; set; }
        public uint Location { get; set; }
        public string PhysicalName { get; set; } = string.Empty;
        public string PhysicalType { get; set; } = string.Empty;
        public List<InterstagePair> Members { get; init; } = [];
    }
}
