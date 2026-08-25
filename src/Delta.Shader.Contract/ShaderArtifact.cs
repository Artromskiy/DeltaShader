using System.Buffers.Binary;

namespace Delta.Shader.Contract;

public interface IShaderArtifact
{
    int FormatVersion { get; }

    ShaderStage Stage { get; }

    string EntryPoint { get; }

    ReadOnlySpan<byte> Spirv { get; }

    ShaderAbi Abi { get; }
}

public sealed class ShaderArtifact : IShaderArtifact
{
    public const int CurrentFormatVersion = 1;
    private const uint SpirvMagic = 0x07230203;

    private readonly byte[] _spirv;

    public ShaderArtifact(
        ReadOnlySpan<byte> spirv,
        string entryPoint,
        ShaderAbi abi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentNullException.ThrowIfNull(abi);
        if (spirv.IsEmpty || spirv.Length % sizeof(uint) != 0)
        {
            throw new ArgumentException("SPIR-V must contain complete 32-bit words.", nameof(spirv));
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(spirv) != SpirvMagic)
        {
            throw new ArgumentException("SPIR-V has an invalid magic word.", nameof(spirv));
        }

        _spirv = spirv.ToArray();
        EntryPoint = entryPoint;
        Abi = abi;
    }

    public int FormatVersion => CurrentFormatVersion;

    public ShaderStage Stage => Abi.Stage;

    public string EntryPoint { get; }

    public ReadOnlySpan<byte> Spirv => _spirv;

    public ShaderAbi Abi { get; }

    public byte[] CopySpirv() => (byte[])_spirv.Clone();
}

public interface IGraphicsShaderProgram
{
    IShaderArtifact Vertex { get; }

    IShaderArtifact Fragment { get; }
}

public sealed class GraphicsShaderProgram : IGraphicsShaderProgram
{
    public GraphicsShaderProgram(IShaderArtifact vertex, IShaderArtifact fragment)
    {
        ArgumentNullException.ThrowIfNull(vertex);
        ArgumentNullException.ThrowIfNull(fragment);
        if (vertex.Stage != ShaderStage.Vertex)
        {
            throw new ArgumentException("The first artifact must be a vertex shader.", nameof(vertex));
        }

        if (fragment.Stage != ShaderStage.Fragment)
        {
            throw new ArgumentException("The second artifact must be a fragment shader.", nameof(fragment));
        }

        ValidateStageInterface(vertex.Abi.Outputs, fragment.Abi.Inputs);

        Vertex = vertex;
        Fragment = fragment;
    }

    public IShaderArtifact Vertex { get; }

    public IShaderArtifact Fragment { get; }

    private static void ValidateStageInterface(
        IReadOnlyList<ShaderInterfaceVariable> vertexOutputs,
        IReadOnlyList<ShaderInterfaceVariable> fragmentInputs)
    {
        foreach (ShaderInterfaceVariable input in fragmentInputs)
        {
            if (input.Location is not uint location || input.Builtin != ShaderBuiltin.None)
            {
                continue;
            }

            bool found = false;
            foreach (ShaderInterfaceVariable output in vertexOutputs)
            {
                if (output.Location != location || output.Builtin != ShaderBuiltin.None)
                {
                    continue;
                }

                if (output.Type != input.Type)
                {
                    throw new ArgumentException($"Vertex and fragment interfaces do not match at location {location}.");
                }

                found = true;
                break;
            }

            if (!found)
            {
                throw new ArgumentException($"Vertex and fragment interfaces do not match at location {location}.");
            }
        }
    }
}
