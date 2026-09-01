using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler;

public static class ShaderCompositeCompiler
{
    public static ShaderCompositeCompilationResult Compose(
        IReadOnlyList<ShaderCompilationResult> vertexLayers,
        IReadOnlyList<ShaderCompilationResult> fragmentLayers)
    {
        if (vertexLayers is null)
        {
            throw new ArgumentNullException(nameof(vertexLayers));
        }
        if (fragmentLayers is null)
        {
            throw new ArgumentNullException(nameof(fragmentLayers));
        }

        var layers = vertexLayers.Concat(fragmentLayers).ToArray();
        var context = ShaderCompositeContextResolver.Resolve(layers);
        var diagnostics = new List<ShaderDiagnostic>(context.Diagnostics);
        var vertices = GetValidModules(vertexLayers, ShaderStage.Vertex, diagnostics);
        var fragments = GetValidModules(fragmentLayers, ShaderStage.Fragment, diagnostics);
        if (vertices.Count == 0 || fragments.Count == 0)
        {
            return new ShaderCompositeCompilationResult(false, context, null, null, diagnostics);
        }

        var logicalFields = context.Fields
            .Where(field => field.Kind == ShaderCompositeContextFieldKind.Interstage &&
                field.GlslType.Length > 0)
            .ToArray();
        var names = CreateLogicalNames(logicalFields);
        var resources = MergeResources(layers, diagnostics);
        var pushConstants = MergePushConstants(layers, diagnostics);
        var vertex = CreateStageModule(
            vertices,
            ShaderStage.Vertex,
            logicalFields,
            names,
            resources,
            pushConstants,
            diagnostics);
        var fragment = CreateStageModule(
            fragments,
            ShaderStage.Fragment,
            logicalFields,
            names,
            resources,
            pushConstants,
            diagnostics);

        var success = diagnostics.Count == 0;
        return new ShaderCompositeCompilationResult(success, context, vertex, fragment, diagnostics);
    }

    private static IReadOnlyList<ShaderIrModule> GetValidModules(
        IReadOnlyList<ShaderCompilationResult> results,
        ShaderStage stage,
        List<ShaderDiagnostic> diagnostics)
    {
        var modules = new List<ShaderIrModule>(results.Count);
        foreach (var result in results)
        {
            if (!result.Success || result.Module is null || result.Module.Stage != stage)
            {
                diagnostics.AddRange(result.Diagnostics);
                continue;
            }

            modules.Add(result.Module);
        }

        return modules;
    }

    private static Dictionary<string, string> CreateLogicalNames(
        IReadOnlyList<ShaderCompositeContextField> fields)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var field in fields)
        {
            var name = IsPosition(field)
                ? "gl_Position"
                : "composite_field_" + index++.ToString(System.Globalization.CultureInfo.InvariantCulture);
            names[field.Identity] = name;
        }

        return names;
    }

    private static IReadOnlyList<ShaderIrResource> MergeResources(
        IReadOnlyList<ShaderCompilationResult> layers,
        List<ShaderDiagnostic> diagnostics)
    {
        var merged = new List<ShaderIrResource>();
        var byIdentity = new Dictionary<string, ShaderIrResource>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            if (!layer.Success || layer.Module is null)
            {
                continue;
            }

            foreach (var resource in layer.Module.Resources)
            {
                var identity = ResourceIdentity(resource);
                if (!byIdentity.TryGetValue(identity, out var existing))
                {
                    existing = resource;
                    byIdentity.Add(identity, existing);
                    merged.Add(existing);
                    continue;
                }

                if (!string.Equals(existing.GlslType, resource.GlslType, StringComparison.Ordinal) ||
                    existing.Category != resource.Category ||
                    existing.Access != resource.Access)
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH005,
                        $"Composite resource '{identity}' has incompatible declarations.",
                        Severity: ShaderDiagnosticSeverity.Error));
                }
                else if (existing.Set != resource.Set || existing.Binding != resource.Binding)
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH005,
                        $"Composite resource '{identity}' has conflicting descriptor bindings.",
                        Severity: ShaderDiagnosticSeverity.Error));
                }
            }
        }

        return merged;
    }

    private static IReadOnlyList<ShaderIrPushConstant> MergePushConstants(
        IReadOnlyList<ShaderCompilationResult> layers,
        List<ShaderDiagnostic> diagnostics)
    {
        var merged = new List<ShaderIrPushConstant>();
        var byIdentity = new Dictionary<string, ShaderIrPushConstant>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            if (!layer.Success || layer.Module is null)
            {
                continue;
            }

            foreach (var push in layer.Module.PushConstants)
            {
                var identity = push.TypeIdentity.Length > 0 ? push.TypeIdentity : push.GlslType;
                if (!byIdentity.TryGetValue(identity, out var existing))
                {
                    byIdentity.Add(identity, push);
                    merged.Add(push);
                    continue;
                }

                if (existing.Alignment != push.Alignment || existing.Size != push.Size ||
                    existing.ArrayStride != push.ArrayStride ||
                    !MembersMatch(existing.Members, push.Members))
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH013,
                        $"Composite push constant '{identity}' has incompatible layouts.",
                        Severity: ShaderDiagnosticSeverity.Error));
                }
            }
        }

        if (merged.Count > 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                ShaderDiagnosticId.DSH013,
                "A composite currently supports one compatible push-constant root across its selected layers.",
                Severity: ShaderDiagnosticSeverity.Error));
        }

        return merged.Count > 0 ? [merged[0]] : [];
    }

    private static ShaderIrModule CreateStageModule(
        IReadOnlyList<ShaderIrModule> layers,
        ShaderStage stage,
        IReadOnlyList<ShaderCompositeContextField> logicalFields,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyList<ShaderIrResource> resources,
        IReadOnlyList<ShaderIrPushConstant> pushConstants,
        List<ShaderDiagnostic> diagnostics)
    {
        var first = layers[0];
        var stageResources = ResourcesForStage(layers, stage, resources);
        var stagePushConstants = PushConstantsForStage(layers, stage, pushConstants);
        var vertexInputs = stage == ShaderStage.Vertex
            ? MergeVertexInputs(layers, diagnostics)
            : [];
        var inputs = stage == ShaderStage.Fragment
            ? CreateFragmentInputs(logicalFields, names)
            : first.Inputs;
        var outputs = stage == ShaderStage.Vertex
            ? CreateVertexOutputs(logicalFields, names)
            : [new ShaderIrInterfaceVariable
            {
                Name = "FragmentColor",
                ParameterName = "return",
                GlslType = "vec4",
                GlslName = "fragColor",
                Location = 0,
                Builtin = "FragmentColor"
            }];
        var bodyParts = layers.Select(layer => RewriteLayerBody(layer, stage, logicalFields, names, stageResources)).ToArray();
        var helperFunctions = layers
            .SelectMany(layer => layer.HelperFunctions)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ShaderIrModule
        {
            Stage = stage,
            SourceEntryPointName = "composite",
            EntryPointName = "composite",
            LocalSizeX = first.LocalSizeX,
            LocalSizeY = first.LocalSizeY,
            LocalSizeZ = first.LocalSizeZ,
            Resources = stageResources,
            Structs = layers.SelectMany(layer => layer.Structs)
                .GroupBy(structure => structure.GlslName, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray(),
            Requirements = first.Requirements,
            Instructions = ["composite " + stage],
            Body = string.Join(Environment.NewLine, bodyParts.Where(body => body.Length > 0)),
            HelperFunctions = helperFunctions,
            UsesBuiltinInvocationId = first.UsesBuiltinInvocationId,
            InvocationParameterName = first.InvocationParameterName,
            Inputs = inputs,
            VertexInputs = vertexInputs,
            VertexBuffers = first.VertexBuffers,
            Outputs = outputs,
            PushConstants = stagePushConstants,
            ContextFields = logicalFields
                .Where(field => field.Stages.Contains(stage))
                .Select(ToIrField)
                .ToArray()
        };
    }

    private static IReadOnlyList<ShaderIrResource> ResourcesForStage(
        IReadOnlyList<ShaderIrModule> layers,
        ShaderStage stage,
        IReadOnlyList<ShaderIrResource> resources)
        => resources
            .Where(resource => layers.Any(layer => layer.Stage == stage &&
                layer.Resources.Any(layerResource => ResourceIdentity(layerResource) == ResourceIdentity(resource))))
            .Select(resource => new ShaderIrResource
            {
                Name = resource.Name,
                ParameterName = resource.ParameterName,
                TypeIdentity = resource.TypeIdentity,
                Category = resource.Category,
                Stage = stage,
                Set = resource.Set,
                Binding = resource.Binding,
                GlslType = resource.GlslType,
                Access = resource.Access,
                ReadOnly = resource.ReadOnly,
                Layout = resource.Layout,
                Std430Layout = resource.Std430Layout,
                Members = resource.Members
            })
            .ToArray();

    private static IReadOnlyList<ShaderIrPushConstant> PushConstantsForStage(
        IReadOnlyList<ShaderIrModule> layers,
        ShaderStage stage,
        IReadOnlyList<ShaderIrPushConstant> pushConstants)
        => pushConstants
            .Where(push => layers.Any(layer => layer.Stage == stage &&
                layer.PushConstants.Any(layerPush => PushConstantsMatch(layerPush, push))))
            .ToArray();

    private static string ResourceIdentity(ShaderIrResource resource)
        => resource.TypeIdentity.Length > 0
            ? resource.TypeIdentity
            : resource.Category + ":" + (resource.GlslType ?? string.Empty);

    private static bool PushConstantsMatch(ShaderIrPushConstant left, ShaderIrPushConstant right)
        => (left.TypeIdentity.Length > 0 ? left.TypeIdentity : left.GlslType) ==
            (right.TypeIdentity.Length > 0 ? right.TypeIdentity : right.GlslType);

    private static IReadOnlyList<ShaderIrVertexInput> MergeVertexInputs(
        IReadOnlyList<ShaderIrModule> layers,
        List<ShaderDiagnostic> diagnostics)
    {
        var inputs = layers[0].VertexInputs;
        foreach (var layer in layers.Skip(1))
        {
            if (layer.VertexInputs.Count != inputs.Count ||
                layer.VertexInputs.Zip(inputs, (left, right) =>
                    left.Location != right.Location || left.GlslType != right.GlslType || left.ByteOffset != right.ByteOffset)
                    .Any(mismatch => mismatch))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH013,
                    "Composite vertex layers must expose identical host vertex inputs.",
                    Severity: ShaderDiagnosticSeverity.Error));
            }
        }

        return inputs;
    }

    private static IReadOnlyList<ShaderIrInterfaceVariable> CreateFragmentInputs(
        IReadOnlyList<ShaderCompositeContextField> fields,
        IReadOnlyDictionary<string, string> names)
        => fields.Where(field => field.Kind == ShaderCompositeContextFieldKind.Interstage && !IsPosition(field))
            .Select((field, index) => new ShaderIrInterfaceVariable
            {
                Name = field.SourcePath,
                ParameterName = field.SourcePath,
                GlslType = field.GlslType,
                GlslName = names[field.Identity],
                Location = (uint)index
            })
            .ToArray();

    private static IReadOnlyList<ShaderIrInterfaceVariable> CreateVertexOutputs(
        IReadOnlyList<ShaderCompositeContextField> fields,
        IReadOnlyDictionary<string, string> names)
    {
        var outputs = new List<ShaderIrInterfaceVariable>
        {
            new()
            {
                Name = "Position",
                ParameterName = "Position",
                GlslType = "vec4",
                GlslName = "gl_Position",
                Builtin = "Position"
            }
        };
        var location = 0u;
        foreach (var field in fields.Where(field => field.Kind == ShaderCompositeContextFieldKind.Interstage && !IsPosition(field)))
        {
            outputs.Add(new ShaderIrInterfaceVariable
            {
                Name = field.SourcePath,
                ParameterName = field.SourcePath,
                GlslType = field.GlslType,
                GlslName = names[field.Identity],
                Location = location++
            });
        }

        return outputs;
    }

    private static string RewriteLayerBody(
        ShaderIrModule module,
        ShaderStage stage,
        IReadOnlyList<ShaderCompositeContextField> fields,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyList<ShaderIrResource> resources)
    {
        var body = TrimBody(module.Body);
        foreach (var field in module.ContextFields.Where(field => field.Kind == ShaderIrContextFieldKind.Interstage))
        {
            var compositeField = fields.FirstOrDefault(candidate =>
                candidate.Kind == ShaderCompositeContextFieldKind.Interstage &&
                string.Equals(candidate.TypeIdentity, field.TypeIdentity, StringComparison.Ordinal));
            if (compositeField is null || !names.TryGetValue(compositeField.Identity, out var name))
            {
                continue;
            }

            if (!(stage == ShaderStage.Vertex && field.HostProvided))
            {
                body = Replace(body, field.ReadGlslName, GetStageName(name, field, stage));
            }
            body = Replace(body, field.WriteGlslName, GetStageName(name, field, stage));
        }

        foreach (var resource in module.Resources)
        {
            var target = resources.FirstOrDefault(candidate =>
                string.Equals(candidate.TypeIdentity, resource.TypeIdentity, StringComparison.Ordinal) &&
                candidate.Category == resource.Category);
            if (target is not null && !string.Equals(resource.Name, target.Name, StringComparison.Ordinal))
            {
                body = Replace(body, resource.Name, target.Name);
            }
        }

        return body;
    }

    private static ShaderIrContextField ToIrField(ShaderCompositeContextField field)
        => new()
        {
            TypeIdentity = field.TypeIdentity,
            SourcePath = field.SourcePath,
            GlslType = field.GlslType,
            Kind = (ShaderIrContextFieldKind)field.Kind,
            Stage = field.Stages.FirstOrDefault(),
            HostProvided = field.HostProvided,
            ResourceKind = field.ResourceKind,
            Access = field.Access,
            Set = field.Set,
            Binding = field.Binding,
            Alignment = field.Alignment,
            Size = field.Size,
            ArrayStride = field.ArrayStride
        };

    private static string GetStageName(string name, ShaderIrContextField field, ShaderStage stage)
        => stage == ShaderStage.Fragment && field.TypeIdentity.EndsWith("Delta.Shader.Position", StringComparison.Ordinal)
            ? "gl_FragCoord"
            : name;

    private static bool IsPosition(ShaderCompositeContextField field)
        => field.TypeIdentity.EndsWith("Delta.Shader.Position", StringComparison.Ordinal);

    private static bool MembersMatch(
        IReadOnlyList<ShaderIrStructMember> left,
        IReadOnlyList<ShaderIrStructMember> right)
        => left.Count == right.Count && left.Zip(right, (a, b) =>
            a.Name == b.Name && a.GlslType == b.GlslType && a.Offset == b.Offset && a.Size == b.Size)
            .All(match => match);

    private static string TrimBody(string? body)
    {
        var result = (body ?? string.Empty).Trim();
        if (result.StartsWith("{", StringComparison.Ordinal) && result.EndsWith("}", StringComparison.Ordinal))
        {
            return result.Substring(1, result.Length - 2).Trim();
        }

        return result;
    }

    private static string Replace(string body, string source, string target)
    {
        if (source.Length == 0 || string.Equals(source, target, StringComparison.Ordinal))
        {
            return body;
        }

        return Regex.Replace(
            body,
            "(?<![A-Za-z0-9_])" + Regex.Escape(source) + "(?![A-Za-z0-9_])",
            target,
            RegexOptions.CultureInvariant);
    }
}

public sealed class ShaderCompositeCompilationResult
{
    public ShaderCompositeCompilationResult(
        bool success,
        ShaderCompositeContextResolution context,
        ShaderIrModule? vertex,
        ShaderIrModule? fragment,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        Success = success;
        Context = context;
        Vertex = vertex;
        Fragment = fragment;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }
    public ShaderCompositeContextResolution Context { get; }
    public ShaderIrModule? Vertex { get; }
    public ShaderIrModule? Fragment { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }

    public ShaderCompilationManifest GetBuildManifest(
        ShaderStage stage,
        ShaderCompilationOptions? options = null)
    {
        var module = stage switch
        {
            ShaderStage.Vertex => Vertex,
            ShaderStage.Fragment => Fragment,
            _ => null
        };
        if (module is null)
        {
            throw new InvalidOperationException($"Composite has no {stage} module.");
        }

        return ShaderManifest.FromModule(module)
            .ToBuildManifest(options ?? ShaderCompilationOptions.Default);
    }
}
