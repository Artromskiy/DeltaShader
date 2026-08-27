using System.Collections.ObjectModel;

namespace Delta.Shader.Contract;

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

        if (offset % 4 != 0)
        {
            throw new ArgumentException("Push-constant offsets must be multiples of four.", nameof(offset));
        }

        if (stages == ShaderStageMask.None)
        {
            throw new ArgumentException("At least one shader stage is required.", nameof(stages));
        }

        if (layout.Size != size)
        {
            throw new ArgumentException("Push-constant layout size must match the range size.", nameof(layout));
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

        if (type.BitWidth % 8 != 0)
        {
            throw new ArgumentException("Specialization constant bit width must be a whole number of bytes.", nameof(type));
        }

        var expectedSize = checked((int)((ulong)(type.BitWidth / 8) * type.VectorSize * type.Columns));
        if (defaultValue.Length != expectedSize)
        {
            throw new ArgumentException($"Specialization constant default value must contain exactly {expectedSize} bytes.", nameof(defaultValue));
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

        ValidateResources(_resources, stage);
        ValidatePushConstants(_pushConstants, stage);
        ValidateInterfaces(_inputs, "input");
        ValidateInterfaces(_outputs, "output");
        ValidateVertexInputs(_vertexInputs, _vertexBuffers, stage);
        ValidateSpecializationConstants(_specializationConstants);
    }

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

    private static void ValidateResources(IReadOnlyList<ShaderResourceBinding> resources, ShaderStage stage)
    {
        var stageFlag = ToStageMask(stage);
        var bindings = new HashSet<ShaderBinding>();
        foreach (var resource in resources)
        {
            if (resource is null)
            {
                throw new ArgumentException("Shader resources cannot contain null entries.", nameof(resources));
            }

            if ((resource.Stages & stageFlag) == 0)
            {
                throw new ArgumentException($"Resource {resource.Binding.Set}:{resource.Binding.Binding} does not include the ABI stage.", nameof(resources));
            }

            if (!bindings.Add(resource.Binding))
            {
                throw new ArgumentException($"Shader resource binding {resource.Binding.Set}:{resource.Binding.Binding} is duplicated.", nameof(resources));
            }
        }
    }

    private static void ValidatePushConstants(IReadOnlyList<ShaderPushConstantRange> pushConstants, ShaderStage stage)
    {
        var stageFlag = ToStageMask(stage);
        foreach (var pushConstant in pushConstants)
        {
            if (pushConstant is null)
            {
                throw new ArgumentException("Push constants cannot contain null entries.", nameof(pushConstants));
            }

            if ((pushConstant.Stages & stageFlag) == 0)
            {
                throw new ArgumentException("A push-constant range does not include the ABI stage.", nameof(pushConstants));
            }
        }
    }

    private static void ValidateInterfaces(IReadOnlyList<ShaderInterfaceVariable> interfaces, string role)
    {
        var locations = new HashSet<uint>();
        foreach (var variable in interfaces)
        {
            if (variable.Builtin == ShaderBuiltin.None && variable.Location is uint location && !locations.Add(location))
            {
                throw new ArgumentException($"Shader {role} location {location} is duplicated.", nameof(interfaces));
            }
        }
    }

    private static void ValidateVertexInputs(
        ReadOnlyCollection<ShaderVertexInput> inputs,
        ReadOnlyCollection<ShaderVertexBufferLayout> buffers,
        ShaderStage stage)
    {
        if (inputs.Count == 0 && buffers.Count == 0)
        {
            return;
        }

        if (stage != ShaderStage.Vertex)
        {
            throw new ArgumentException("Vertex inputs and buffers are only valid for vertex shaders.", nameof(stage));
        }

        var bufferBindings = new HashSet<uint>();
        foreach (var buffer in buffers)
        {
            if (buffer.Stride == 0 || !bufferBindings.Add(buffer.Binding))
            {
                throw new ArgumentException("Vertex buffer bindings must be unique and have a non-zero stride.", nameof(buffers));
            }
        }

        var locations = new HashSet<uint>();
        foreach (var input in inputs)
        {
            if (!locations.Add(input.Location) || !bufferBindings.Contains(input.Binding))
            {
                throw new ArgumentException("Vertex input locations must be unique and refer to a declared buffer binding.", nameof(inputs));
            }
        }
    }

    private static void ValidateSpecializationConstants(IReadOnlyList<ShaderSpecializationConstant> constants)
    {
        var ids = new HashSet<uint>();
        foreach (var constant in constants)
        {
            if (constant is null || !ids.Add(constant.Id))
            {
                throw new ArgumentException("Specialization constant ids must be unique and non-null.", nameof(constants));
            }
        }
    }

    private static ShaderStageMask ToStageMask(ShaderStage stage)
        => stage switch
        {
            ShaderStage.Compute => ShaderStageMask.Compute,
            ShaderStage.Vertex => ShaderStageMask.Vertex,
            ShaderStage.Fragment => ShaderStageMask.Fragment,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown shader stage.")
        };

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T>? values)
    {
        var array = values?.ToArray() ?? [];
        if (array.Any(value => value is null))
        {
            throw new ArgumentException("Shader ABI collections cannot contain null entries.", nameof(values));
        }

        return Array.AsReadOnly(array);
    }
}
