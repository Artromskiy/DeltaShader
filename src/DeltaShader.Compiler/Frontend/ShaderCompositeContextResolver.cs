using System;
using System.Collections.Generic;
using System.Linq;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler;

public static class ShaderCompositeContextResolver
{
    public static ShaderCompositeContextResolution Resolve(
        IReadOnlyList<ShaderCompilationResult> layers)
    {
        if (layers is null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        var diagnostics = new List<ShaderDiagnostic>();
        var merged = new Dictionary<string, MergedField>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            if (!layer.Success || layer.Module is null)
            {
                diagnostics.AddRange(layer.Diagnostics);
                continue;
            }

            foreach (var field in layer.Module.ContextFields)
            {
                if (field.TypeIdentity.Length == 0)
                {
                    diagnostics.Add(new ShaderDiagnostic(
                        ShaderDiagnosticId.DSH013,
                        $"Composite context field '{field.SourcePath}' has no full type identity.",
                        Severity: ShaderDiagnosticSeverity.Error));
                    continue;
                }

                var key = CreateKey(field);
                if (!merged.TryGetValue(key, out var target))
                {
                    target = new MergedField(field);
                    merged.Add(key, target);
                }
                else
                {
                    target.Merge(field, diagnostics);
                }

                if (!target.Stages.Contains(layer.Module.Stage))
                {
                    target.Stages.Add(layer.Module.Stage);
                }

                if (!target.Layers.Contains(layer.SourceMethodIdentity, StringComparer.Ordinal))
                {
                    target.Layers.Add(layer.SourceMethodIdentity);
                }
            }
        }

        var fields = merged.Values.Select(field => field.ToPublic()).ToArray();
        ValidateInterstageProducers(fields, diagnostics);
        return new ShaderCompositeContextResolution(diagnostics.Count == 0, fields, diagnostics);
    }

    private static string CreateKey(ShaderIrContextField field)
        => field.Kind + ":" + field.TypeIdentity;

    private static void ValidateInterstageProducers(
        IReadOnlyList<ShaderCompositeContextField> fields,
        List<ShaderDiagnostic> diagnostics)
    {
        var vertexFields = new HashSet<string>(fields
            .Where(field => field.Kind == ShaderCompositeContextFieldKind.Interstage &&
                field.Stages.Contains(ShaderStage.Vertex))
            .Select(field => field.TypeIdentity),
            StringComparer.Ordinal);
        foreach (var field in fields.Where(field =>
            field.Kind == ShaderCompositeContextFieldKind.Interstage &&
            field.Stages.Contains(ShaderStage.Fragment) &&
            !field.TypeIdentity.EndsWith("Delta.Shader.Position", StringComparison.Ordinal)))
        {
            if (!vertexFields.Contains(field.TypeIdentity))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH013,
                    $"Composite fragment field '{field.TypeIdentity}' has no vertex producer.",
                    Severity: ShaderDiagnosticSeverity.Error));
            }
        }
    }

    private sealed class MergedField
    {
        private readonly ShaderCompositeContextFieldKind kind;
        private readonly string identity;
        private readonly string typeIdentity;
        private readonly string glslType;
        private readonly ShaderResourceKind resourceKind;
        private readonly ShaderResourceAccess access;
        private readonly string sourcePath;
        private readonly uint alignment;
        private readonly uint size;
        private readonly uint arrayStride;
        private uint? set;
        private uint? binding;
        private bool hostProvided;

        public MergedField(ShaderIrContextField source)
        {
            kind = (ShaderCompositeContextFieldKind)source.Kind;
            identity = CreateKey(source);
            typeIdentity = source.TypeIdentity;
            sourcePath = source.SourcePath;
            glslType = source.GlslType;
            resourceKind = source.ResourceKind;
            access = source.Access;
            alignment = source.Alignment;
            size = source.Size;
            arrayStride = source.ArrayStride;
            set = source.Set;
            binding = source.Binding;
            hostProvided = source.HostProvided;
            Stages = [source.Stage];
            Layers = [];
        }

        public List<ShaderStage> Stages { get; }
        public List<string> Layers { get; }

        public void Merge(ShaderIrContextField source, List<ShaderDiagnostic> diagnostics)
        {
            if (!string.Equals(glslType, source.GlslType, StringComparison.Ordinal))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH013,
                    $"Composite field '{typeIdentity}' has incompatible GLSL types '{glslType}' and '{source.GlslType}'.",
                    Severity: ShaderDiagnosticSeverity.Error));
            }

            if (kind == ShaderCompositeContextFieldKind.Resource &&
                (resourceKind != source.ResourceKind || access != source.Access))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH005,
                    $"Composite resource '{typeIdentity}' has incompatible kind or access across layers.",
                    Severity: ShaderDiagnosticSeverity.Error));
            }

            if (kind == ShaderCompositeContextFieldKind.PushConstant &&
                (alignment != source.Alignment || size != source.Size || arrayStride != source.ArrayStride))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    ShaderDiagnosticId.DSH013,
                    $"Composite push constant '{typeIdentity}' has incompatible layout across layers.",
                    Severity: ShaderDiagnosticSeverity.Error));
            }

            if (set != source.Set || binding != source.Binding)
            {
                set = null;
                binding = null;
            }

            hostProvided |= source.HostProvided;
        }

        public ShaderCompositeContextField ToPublic()
            => new()
            {
                Identity = identity,
                TypeIdentity = typeIdentity,
                SourcePath = sourcePath,
                GlslType = glslType,
                Kind = kind,
                Stages = Stages.ToArray(),
                HostProvided = hostProvided,
                ResourceKind = resourceKind,
                Access = access,
                Set = set,
                Binding = binding,
                Alignment = alignment,
                Size = size,
                ArrayStride = arrayStride,
                LayerIdentities = Layers.ToArray()
            };

    }
}

public enum ShaderCompositeContextFieldKind
{
    Interstage,
    Resource,
    PushConstant
}

public sealed class ShaderCompositeContextResolution
{
    public ShaderCompositeContextResolution(
        bool success,
        IReadOnlyList<ShaderCompositeContextField> fields,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        Success = success;
        Fields = fields;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }
    public IReadOnlyList<ShaderCompositeContextField> Fields { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
}

public sealed class ShaderCompositeContextField
{
    public string Identity { get; init; } = string.Empty;
    public string TypeIdentity { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public ShaderCompositeContextFieldKind Kind { get; init; }
    public IReadOnlyList<ShaderStage> Stages { get; init; } = [];
    public bool HostProvided { get; init; }
    public ShaderResourceKind ResourceKind { get; init; } = ShaderResourceKind.None;
    public ShaderResourceAccess Access { get; init; } = ShaderResourceAccess.ReadWrite;
    public uint? Set { get; init; }
    public uint? Binding { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<string> LayerIdentities { get; init; } = [];
}
