namespace Delta.Shader.Contract;

public enum ShaderStage
{
    Unknown,
    Compute,
    Vertex,
    Fragment,
}

[Flags]
public enum ShaderStageMask
{
    None = 0,
    Compute = 1 << 0,
    Vertex = 1 << 1,
    Fragment = 1 << 2,
    AllGraphics = Vertex | Fragment,
    All = Compute | AllGraphics,
}

public enum ShaderResourceKind
{
    Unknown,
    UniformBuffer,
    StorageBuffer,
    SampledTexture,
    StorageTexture,
    Sampler,
    CombinedTextureSampler,
}

[Flags]
public enum ShaderResourceAccess
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}

[Flags]
public enum ShaderCapabilities : ulong
{
    None = 0,
    Integer64 = 1UL << 0,
    DoublePrecisionFloatingPoint = 1UL << 1,
    HalfPrecisionFloatingPoint = 1UL << 2,
    Integer16 = 1UL << 3,
    Integer8 = 1UL << 4,
    StorageImageWriteWithoutFormat = 1UL << 5,
    Subgroup = 1UL << 6,
    DrawParameters = 1UL << 7,
}

public enum ShaderValueKind
{
    Unknown,
    Boolean,
    SignedInteger,
    UnsignedInteger,
    FloatingPoint,
    Structure,
}

public enum ShaderBuiltin
{
    None,
    Unknown,
    Position,
    VertexIndex,
    InstanceIndex,
    FragmentCoordinate,
    FrontFacing,
    FragmentDepth,
    GlobalInvocationId,
    LocalInvocationId,
    WorkgroupId,
}

public enum ShaderVertexInputRate
{
    Vertex,
    Instance,
}

public readonly record struct ShaderBinding(uint Set, uint Binding);

public readonly record struct ShaderValueType(
    ShaderValueKind Kind,
    uint BitWidth,
    uint VectorSize = 1,
    uint Columns = 1)
{
    public static ShaderValueType Structure => new(ShaderValueKind.Structure, 0, 0, 0);

    public bool IsValid => Kind == ShaderValueKind.Structure
        ? BitWidth == 0 && VectorSize == 0 && Columns == 0
        : Kind != ShaderValueKind.Unknown && BitWidth > 0 &&
          VectorSize is >= 1 and <= 4 && Columns is >= 1 and <= 4;
}

public readonly record struct ShaderWorkgroupSize(uint X, uint Y, uint Z)
{
    public bool IsValid => X > 0 && Y > 0 && Z > 0;
}
