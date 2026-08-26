using System.Collections.ObjectModel;

namespace Delta.Shader.Abstractions;

public enum ShaderStage
{
    Compute,
    Vertex,
    Fragment
}

public sealed class ShaderArtifact
{
    public const int CurrentFormatVersion = 1;

    public ShaderArtifact(byte[] spirv, ShaderAbiManifest manifest)
    {
        ArgumentGuard.NotNull(spirv, nameof(spirv));
        if (spirv.Length == 0)
        {
            throw new ArgumentException("SPIR-V artifact cannot be empty.", nameof(spirv));
        }

        ArgumentGuard.NotNull(manifest, nameof(manifest));

        if (manifest.Version != ShaderAbiManifest.CurrentVersion)
        {
            throw new ArgumentException($"Unsupported shader ABI manifest version '{manifest.Version}'.", nameof(manifest));
        }

        SpirvBytes = (byte[])spirv.Clone();
        Manifest = CloneManifest(manifest);
    }

    public int FormatVersion => CurrentFormatVersion;
    public ReadOnlySpan<byte> Spirv => SpirvBytes;
    public ShaderAbiManifest Manifest { get; }
    public ShaderStage Stage => Manifest.Stage;
    public string EntryPoint => Manifest.EntryPointName;

    public byte[] GetSpirvForUpload() => (byte[])SpirvBytes.Clone();

    private byte[] SpirvBytes { get; }

    private static ShaderAbiManifest CloneManifest(ShaderAbiManifest source)
    {
        var resources = source.Resources?.Select(CloneResource).ToArray() ?? Array.Empty<ShaderAbiResource>();
        var inputs = source.Inputs?.Select(CloneInterfaceVariable).ToArray() ?? Array.Empty<ShaderAbiInterfaceVariable>();
        var vertexInputs = source.VertexInputs?.Select(CloneVertexInput).ToArray() ?? Array.Empty<ShaderAbiVertexInput>();
        var bindings = source.VertexBufferBindings?.Select(CloneVertexBufferBinding).ToArray() ?? Array.Empty<ShaderAbiVertexBufferBinding>();
        var outputs = source.Outputs?.Select(CloneInterfaceVariable).ToArray() ?? Array.Empty<ShaderAbiInterfaceVariable>();
        var pushConstants = source.PushConstants?.Select(ClonePushConstant).ToArray() ?? Array.Empty<ShaderAbiPushConstant>();

        return new ShaderAbiManifest
        {
            Version = source.Version,
            Stage = source.Stage,
            SourceEntryPointName = source.SourceEntryPointName,
            EntryPointName = source.EntryPointName,
            TargetProfile = source.TargetProfile,
            GlslVersion = source.GlslVersion,
            SpirvVersion = source.SpirvVersion,
            StorageLayout = source.StorageLayout,
            LocalSizeX = source.LocalSizeX,
            LocalSizeY = source.LocalSizeY,
            LocalSizeZ = source.LocalSizeZ,
            Resources = new ReadOnlyCollection<ShaderAbiResource>(resources),
            Inputs = new ReadOnlyCollection<ShaderAbiInterfaceVariable>(inputs),
            VertexInputs = new ReadOnlyCollection<ShaderAbiVertexInput>(vertexInputs),
            VertexBufferBindings = new ReadOnlyCollection<ShaderAbiVertexBufferBinding>(bindings),
            Outputs = new ReadOnlyCollection<ShaderAbiInterfaceVariable>(outputs),
            PushConstants = new ReadOnlyCollection<ShaderAbiPushConstant>(pushConstants)
        };
    }

    private static ShaderAbiResource CloneResource(ShaderAbiResource source) => new()
    {
        Name = source.Name,
        ParameterName = source.ParameterName,
        Category = source.Category,
        Stage = source.Stage,
        Set = source.Set,
        Binding = source.Binding,
        GlslType = source.GlslType,
        ReadOnly = source.ReadOnly,
        Access = source.Access,
        Layout = source.Layout,
        Offset = source.Offset,
        Alignment = source.Alignment,
        Size = source.Size,
        ArrayStride = source.ArrayStride,
        MatrixStride = source.MatrixStride,
        Members = new ReadOnlyCollection<ShaderAbiMember>((source.Members ?? Array.Empty<ShaderAbiMember>()).Select(CloneMember).ToArray()),
        Packing = ClonePacking(source.Packing)
    };

    private static ShaderAbiPushConstant ClonePushConstant(ShaderAbiPushConstant source) => new()
    {
        Name = source.Name,
        ParameterName = source.ParameterName,
        GlslType = source.GlslType,
        Alignment = source.Alignment,
        Size = source.Size,
        ArrayStride = source.ArrayStride,
        Members = new ReadOnlyCollection<ShaderAbiMember>((source.Members ?? Array.Empty<ShaderAbiMember>()).Select(CloneMember).ToArray())
    };

    private static ShaderAbiMember CloneMember(ShaderAbiMember source) => new()
    {
        Name = source.Name,
        GlslName = source.GlslName,
        GlslType = source.GlslType,
        Offset = source.Offset,
        Alignment = source.Alignment,
        Size = source.Size,
        ArrayStride = source.ArrayStride,
        MatrixStride = source.MatrixStride,
        HostRepresentation = source.HostRepresentation,
        Members = new ReadOnlyCollection<ShaderAbiMember>((source.Members ?? Array.Empty<ShaderAbiMember>()).Select(CloneMember).ToArray())
    };

    private static ShaderAbiPackingPlan ClonePacking(ShaderAbiPackingPlan source) => new()
    {
        Scheme = source.Scheme,
        Strategy = source.Strategy,
        DirectRawUploadAllowed = source.DirectRawUploadAllowed,
        BoolRepresentation = source.BoolRepresentation,
        Stride = source.Stride
    };

    private static ShaderAbiInterfaceVariable CloneInterfaceVariable(ShaderAbiInterfaceVariable source) => new()
    {
        Name = source.Name,
        ParameterName = source.ParameterName,
        GlslName = source.GlslName,
        GlslType = source.GlslType,
        Location = source.Location,
        Builtin = source.Builtin
    };

    private static ShaderAbiVertexInput CloneVertexInput(ShaderAbiVertexInput source) => new()
    {
        Name = source.Name,
        ParameterName = source.ParameterName,
        GlslName = source.GlslName,
        GlslType = source.GlslType,
        Location = source.Location,
        Binding = source.Binding,
        ByteOffset = source.ByteOffset,
        InputRate = source.InputRate,
        ByteSize = source.ByteSize,
        Alignment = source.Alignment,
        FormatHint = source.FormatHint
    };

    private static ShaderAbiVertexBufferBinding CloneVertexBufferBinding(ShaderAbiVertexBufferBinding source) => new()
    {
        Binding = source.Binding,
        Stride = source.Stride,
        InputRate = source.InputRate,
        Attributes = new ReadOnlyCollection<ShaderAbiVertexInput>((source.Attributes ?? Array.Empty<ShaderAbiVertexInput>()).Select(CloneVertexInput).ToArray())
    };
}

public sealed class GraphicsShaderProgram
{
    public GraphicsShaderProgram(ShaderArtifact vertex, ShaderArtifact fragment)
    {
        Vertex = vertex ?? throw new ArgumentNullException(nameof(vertex));
        Fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
        if (vertex.Stage != ShaderStage.Vertex)
        {
            throw new ArgumentException("The first artifact must contain a vertex stage.", nameof(vertex));
        }

        if (fragment.Stage != ShaderStage.Fragment)
        {
            throw new ArgumentException("The second artifact must contain a fragment stage.", nameof(fragment));
        }

        ValidateSharedLayouts(vertex.Manifest, fragment.Manifest);
    }

    public ShaderArtifact Vertex { get; }
    public ShaderArtifact Fragment { get; }

    private static void ValidateSharedLayouts(ShaderAbiManifest vertex, ShaderAbiManifest fragment)
    {
        var fragmentResources = fragment.Resources.ToDictionary(resource => (resource.Set, resource.Binding));
        foreach (var resource in vertex.Resources)
        {
            if (fragmentResources.TryGetValue((resource.Set, resource.Binding), out var paired) && !SameResourceLayout(resource, paired))
            {
                throw new ArgumentException($"Graphics resource set {resource.Set}, binding {resource.Binding} has incompatible layouts.");
            }
        }

        if (vertex.PushConstants.Count > 0 && fragment.PushConstants.Count > 0 &&
            (vertex.PushConstants.Count != fragment.PushConstants.Count ||
             !SamePushConstantLayouts(vertex.PushConstants, fragment.PushConstants)))
        {
            throw new ArgumentException("Vertex and fragment push-constant layouts are incompatible.");
        }
    }

    private static bool SameResourceLayout(ShaderAbiResource left, ShaderAbiResource right) =>
        left.Category == right.Category && left.GlslType == right.GlslType && left.Layout == right.Layout &&
        left.Offset == right.Offset && left.Alignment == right.Alignment && left.Size == right.Size &&
        left.ArrayStride == right.ArrayStride && left.MatrixStride == right.MatrixStride && SameMembers(left.Members, right.Members);

    private static bool SamePushConstantLayout(ShaderAbiPushConstant left, ShaderAbiPushConstant right) =>
        left.GlslType == right.GlslType && left.Alignment == right.Alignment && left.Size == right.Size &&
        left.ArrayStride == right.ArrayStride && SameMembers(left.Members, right.Members);

    private static bool SamePushConstantLayouts(IReadOnlyList<ShaderAbiPushConstant> left, IReadOnlyList<ShaderAbiPushConstant> right)
    {
        for (var index = 0; index < left.Count; index++)
        {
            if (!SamePushConstantLayout(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameMembers(IReadOnlyList<ShaderAbiMember> left, IReadOnlyList<ShaderAbiMember> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftMember = left[index];
            var rightMember = right[index];
            if (leftMember.Name != rightMember.Name || leftMember.GlslType != rightMember.GlslType ||
                leftMember.Offset != rightMember.Offset || leftMember.Alignment != rightMember.Alignment ||
                leftMember.Size != rightMember.Size || leftMember.ArrayStride != rightMember.ArrayStride ||
                leftMember.MatrixStride != rightMember.MatrixStride || !SameMembers(leftMember.Members, rightMember.Members))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class ShaderAbiManifest
{
    public const int CurrentVersion = 4;

    public int Version { get; init; } = CurrentVersion;
    public ShaderStage Stage { get; init; } = ShaderStage.Compute;
    public string SourceEntryPointName { get; init; } = string.Empty;
    public string EntryPointName { get; init; } = string.Empty;
    public string TargetProfile { get; init; } = "vulkan1.2";
    public string GlslVersion { get; init; } = "460";
    public string SpirvVersion { get; init; } = "1.5";
    public string StorageLayout { get; init; } = "std430";
    public uint LocalSizeX { get; init; }
    public uint LocalSizeY { get; init; }
    public uint LocalSizeZ { get; init; }
    public IReadOnlyList<ShaderAbiResource> Resources { get; init; } = Array.Empty<ShaderAbiResource>();
    public IReadOnlyList<ShaderAbiInterfaceVariable> Inputs { get; init; } = Array.Empty<ShaderAbiInterfaceVariable>();
    public IReadOnlyList<ShaderAbiVertexInput> VertexInputs { get; init; } = Array.Empty<ShaderAbiVertexInput>();
    public IReadOnlyList<ShaderAbiVertexBufferBinding> VertexBufferBindings { get; init; } = Array.Empty<ShaderAbiVertexBufferBinding>();
    public IReadOnlyList<ShaderAbiInterfaceVariable> Outputs { get; init; } = Array.Empty<ShaderAbiInterfaceVariable>();
    public IReadOnlyList<ShaderAbiPushConstant> PushConstants { get; init; } = Array.Empty<ShaderAbiPushConstant>();
}

public sealed class ShaderAbiInterfaceVariable
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public string? Builtin { get; init; }
}

public sealed class ShaderAbiVertexInput
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Location { get; init; }
    public uint Binding { get; init; }
    public uint ByteOffset { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public uint ByteSize { get; init; }
    public uint Alignment { get; init; }
    public string FormatHint { get; init; } = string.Empty;
}

public sealed class ShaderAbiVertexBufferBinding
{
    public uint Binding { get; init; }
    public uint Stride { get; init; }
    public VertexInputRate InputRate { get; init; } = VertexInputRate.Vertex;
    public IReadOnlyList<ShaderAbiVertexInput> Attributes { get; init; } = Array.Empty<ShaderAbiVertexInput>();
}

public sealed class ShaderAbiPushConstant
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public IReadOnlyList<ShaderAbiMember> Members { get; init; } = Array.Empty<ShaderAbiMember>();
}

public sealed class ShaderAbiResource
{
    public string Name { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public ShaderStage Stage { get; init; }
    public uint Set { get; init; }
    public uint Binding { get; init; }
    public string? GlslType { get; init; }
    public bool ReadOnly { get; init; }
    public ShaderResourceAccess Access { get; init; } = ShaderResourceAccess.ReadWrite;
    public string Layout { get; init; } = "std430";
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public IReadOnlyList<ShaderAbiMember> Members { get; init; } = Array.Empty<ShaderAbiMember>();
    public ShaderAbiPackingPlan Packing { get; init; } = new();
}

public sealed class ShaderAbiPackingPlan
{
    public string Scheme { get; init; } = "std430";
    public string Strategy { get; init; } = "std430-explicit-members";
    public bool DirectRawUploadAllowed { get; init; }
    public string BoolRepresentation { get; init; } = "uint32";
    public uint Stride { get; init; }
}

public sealed class ShaderAbiMember
{
    public string Name { get; init; } = string.Empty;
    public string GlslName { get; init; } = string.Empty;
    public string GlslType { get; init; } = string.Empty;
    public uint Offset { get; init; }
    public uint Alignment { get; init; }
    public uint Size { get; init; }
    public uint ArrayStride { get; init; }
    public uint? MatrixStride { get; init; }
    public string HostRepresentation { get; init; } = "std430";
    public IReadOnlyList<ShaderAbiMember> Members { get; init; } = Array.Empty<ShaderAbiMember>();
}
