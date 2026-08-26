using System.Buffers.Binary;

namespace Delta.Shader.Contract;

public interface IShaderArtifact
{
    ShaderStage Stage { get; }

    string EntryPoint { get; }

    ReadOnlySpan<byte> Spirv { get; }

    ShaderAbi Abi { get; }
}

public sealed class ShaderArtifact : IShaderArtifact
{
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

        ValidateSharedAbi(vertex.Abi, fragment.Abi);
        ValidateStageInterface(vertex.Abi.Outputs, fragment.Abi.Inputs);

        Vertex = vertex;
        Fragment = fragment;
    }

    public IShaderArtifact Vertex { get; }

    public IShaderArtifact Fragment { get; }

    private static void ValidateSharedAbi(ShaderAbi vertex, ShaderAbi fragment)
    {
        foreach (ShaderResourceBinding vertexResource in vertex.Resources)
        {
            foreach (ShaderResourceBinding fragmentResource in fragment.Resources)
            {
                if (vertexResource.Binding != fragmentResource.Binding)
                {
                    continue;
                }

                if (vertexResource.Kind != fragmentResource.Kind ||
                    vertexResource.Access != fragmentResource.Access ||
                    vertexResource.DescriptorCount != fragmentResource.DescriptorCount ||
                    !SameLayout(vertexResource.Layout, fragmentResource.Layout))
                {
                    throw new ArgumentException($"Graphics resource set {vertexResource.Binding.Set}, binding {vertexResource.Binding.Binding} has incompatible layouts.");
                }
            }
        }

        if (vertex.PushConstants.Count == 0 || fragment.PushConstants.Count == 0)
        {
            return;
        }

        if (vertex.PushConstants.Count != fragment.PushConstants.Count)
        {
            throw new ArgumentException("Vertex and fragment push-constant ranges are incompatible.");
        }

        for (var index = 0; index < vertex.PushConstants.Count; index++)
        {
            ShaderPushConstantRange left = vertex.PushConstants[index];
            ShaderPushConstantRange right = fragment.PushConstants[index];
            if (left.Offset != right.Offset || left.Size != right.Size || !SameLayout(left.Layout, right.Layout))
            {
                throw new ArgumentException("Vertex and fragment push-constant ranges are incompatible.");
            }
        }
    }

    private static bool SameLayout(ShaderAbiLayout left, ShaderAbiLayout right)
    {
        if (left.Size != right.Size || left.Alignment != right.Alignment ||
            left.ArrayStride != right.ArrayStride || left.MatrixStride != right.MatrixStride ||
            left.Members.Count != right.Members.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Members.Count; index++)
        {
            if (!SameMember(left.Members[index], right.Members[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameMember(ShaderAbiMember left, ShaderAbiMember right)
    {
        if (left.Type != right.Type || left.Offset != right.Offset || left.Size != right.Size ||
            left.Alignment != right.Alignment || left.ArrayStride != right.ArrayStride ||
            left.MatrixStride != right.MatrixStride)
        {
            return false;
        }

        if (left.NestedLayout is null || right.NestedLayout is null)
        {
            return left.NestedLayout is null && right.NestedLayout is null;
        }

        return SameLayout(left.NestedLayout, right.NestedLayout);
    }

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
