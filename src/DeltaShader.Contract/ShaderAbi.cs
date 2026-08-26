using System.Collections.ObjectModel;

namespace DeltaShader.Contract;

public readonly record struct ShaderInterfaceVariable(
    ShaderValueType Type,
    uint? Location = null,
    ShaderBuiltin Builtin = ShaderBuiltin.None);

public readonly record struct ShaderVertexInput(
    uint Location,
    uint Binding,
    uint ByteOffset,
    ShaderValueType Type,
    ShaderVertexInputRate InputRate = ShaderVertexInputRate.Vertex);

public readonly record struct ShaderVertexBufferLayout(
    uint Binding,
    uint Stride,
    ShaderVertexInputRate InputRate = ShaderVertexInputRate.Vertex);

public sealed class ShaderResourceBinding
{
    public ShaderResourceBinding(
        ShaderBinding binding,
        ShaderResourceKind kind,
        ShaderResourceAccess access,
        ShaderStageMask stages,
        ShaderAbiLayout? layout = null,
        uint descriptorCount = 1)
    {
        if (kind == ShaderResourceKind.Unknown)
        {
            throw new ArgumentException("Shader resource kind is required.", nameof(kind));
        }

        if (access == ShaderResourceAccess.None)
        {
            throw new ArgumentException("Shader resource access is required.", nameof(access));
        }

        if (stages == ShaderStageMask.None)
        {
            throw new ArgumentException("At least one shader stage is required.", nameof(stages));
        }

        ArgumentOutOfRangeException.ThrowIfZero(descriptorCount);

        Binding = binding;
        Kind = kind;
        Access = access;
        Stages = stages;
        Layout = layout ?? ShaderAbiLayout.Empty;
        DescriptorCount = descriptorCount;
    }

    public ShaderBinding Binding { get; }

    public ShaderResourceKind Kind { get; }

    public ShaderResourceAccess Access { get; }

    public ShaderStageMask Stages { get; }

    public ShaderAbiLayout Layout { get; }

    public uint DescriptorCount { get; }
}

public sealed class ShaderPushConstantRange
{
    public ShaderPushConstantRange(
        uint offset,
        uint size,
        ShaderStageMask stages,
        ShaderAbiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfZero(size);

        if (stages == ShaderStageMask.None)
        {
            throw new ArgumentException("At least one shader stage is required.", nameof(stages));
        }

        Offset = offset;
        Size = size;
        Stages = stages;
        Layout = layout;
    }

    public uint Offset { get; }

    public uint Size { get; }

    public ShaderStageMask Stages { get; }

    public ShaderAbiLayout Layout { get; }
}

public sealed class ShaderSpecializationConstant
{
    private readonly byte[] _defaultValue;

    public ShaderSpecializationConstant(
        uint id,
        ShaderValueType type,
        ReadOnlySpan<byte> defaultValue)
    {
        if (!type.IsValid)
        {
            throw new ArgumentException("Specialization constant type is invalid.", nameof(type));
        }

        if (type.Kind == ShaderValueKind.Structure)
        {
            throw new ArgumentException("Specialization constants must be scalar, vector or matrix values.", nameof(type));
        }

        Id = id;
        Type = type;
        _defaultValue = defaultValue.ToArray();
    }

    public uint Id { get; }

    public ShaderValueType Type { get; }

    public ReadOnlySpan<byte> DefaultValue => _defaultValue;
}

public sealed class ShaderAbi
{
    public const int CurrentVersion = 1;

    private readonly ReadOnlyCollection<ShaderResourceBinding> _resources;
    private readonly ReadOnlyCollection<ShaderPushConstantRange> _pushConstants;
    private readonly ReadOnlyCollection<ShaderInterfaceVariable> _inputs;
    private readonly ReadOnlyCollection<ShaderInterfaceVariable> _outputs;
    private readonly ReadOnlyCollection<ShaderVertexInput> _vertexInputs;
    private readonly ReadOnlyCollection<ShaderVertexBufferLayout> _vertexBuffers;
    private readonly ReadOnlyCollection<ShaderSpecializationConstant> _specializationConstants;

    public ShaderAbi(
        ShaderStage stage,
        IEnumerable<ShaderResourceBinding>? resources = null,
        IEnumerable<ShaderPushConstantRange>? pushConstants = null,
        IEnumerable<ShaderInterfaceVariable>? inputs = null,
        IEnumerable<ShaderInterfaceVariable>? outputs = null,
        IEnumerable<ShaderVertexInput>? vertexInputs = null,
        IEnumerable<ShaderVertexBufferLayout>? vertexBuffers = null,
        IEnumerable<ShaderSpecializationConstant>? specializationConstants = null,
        ShaderWorkgroupSize workgroupSize = default,
        ShaderCapabilities requiredCapabilities = ShaderCapabilities.None)
    {
        if (stage == ShaderStage.Unknown)
        {
            throw new ArgumentException("Shader stage is required.", nameof(stage));
        }

        if (stage == ShaderStage.Compute && !workgroupSize.IsValid)
        {
            throw new ArgumentException("Compute shaders require a non-zero workgroup size.", nameof(workgroupSize));
        }

        Stage = stage;
        WorkgroupSize = workgroupSize;
        RequiredCapabilities = requiredCapabilities;
        _resources = Copy(resources);
        _pushConstants = Copy(pushConstants);
        _inputs = Copy(inputs);
        _outputs = Copy(outputs);
        _vertexInputs = Copy(vertexInputs);
        _vertexBuffers = Copy(vertexBuffers);
        _specializationConstants = Copy(specializationConstants);
    }

    public int Version => CurrentVersion;

    public ShaderStage Stage { get; }

    public IReadOnlyList<ShaderResourceBinding> Resources => _resources;

    public IReadOnlyList<ShaderPushConstantRange> PushConstants => _pushConstants;

    public IReadOnlyList<ShaderInterfaceVariable> Inputs => _inputs;

    public IReadOnlyList<ShaderInterfaceVariable> Outputs => _outputs;

    public IReadOnlyList<ShaderVertexInput> VertexInputs => _vertexInputs;

    public IReadOnlyList<ShaderVertexBufferLayout> VertexBuffers => _vertexBuffers;

    public IReadOnlyList<ShaderSpecializationConstant> SpecializationConstants => _specializationConstants;

    public ShaderWorkgroupSize WorkgroupSize { get; }

    public ShaderCapabilities RequiredCapabilities { get; }

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T>? values)
        => Array.AsReadOnly(values?.ToArray() ?? []);
}
