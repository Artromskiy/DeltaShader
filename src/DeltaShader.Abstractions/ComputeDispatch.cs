namespace Delta.Shader.Abstractions;

public readonly struct ComputeDispatchDimensions : IEquatable<ComputeDispatchDimensions>
{
    public ComputeDispatchDimensions(uint x, uint y = 1, uint z = 1)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public uint X { get; }
    public uint Y { get; }
    public uint Z { get; }

    public bool Equals(ComputeDispatchDimensions other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is ComputeDispatchDimensions other && Equals(other);
    public override int GetHashCode() => unchecked((int)(X * 397u ^ Y * 17u ^ Z));
    public static bool operator ==(ComputeDispatchDimensions left, ComputeDispatchDimensions right) => left.Equals(right);
    public static bool operator !=(ComputeDispatchDimensions left, ComputeDispatchDimensions right) => !left.Equals(right);

    public static ComputeDispatchDimensions ForElements(ShaderArtifact artifact, uint elementCount)
    {
        ArgumentGuard.NotNull(artifact, nameof(artifact));
        if (artifact.Stage != ShaderStage.Compute)
        {
            throw new ArgumentException("The shader artifact must contain a compute stage.", nameof(artifact));
        }

        var localSize = artifact.Manifest.LocalSizeX;
        if (localSize == 0 || artifact.Manifest.LocalSizeY == 0 || artifact.Manifest.LocalSizeZ == 0)
        {
            throw new ArgumentException("The compute artifact must declare non-zero local sizes.", nameof(artifact));
        }

        return new ComputeDispatchDimensions(
            elementCount == 0 ? 0 : checked((elementCount + localSize - 1) / localSize),
            1,
            1);
    }
}

public readonly struct ComputeDispatchBinding<TResource> : IEquatable<ComputeDispatchBinding<TResource>>
{
    public ComputeDispatchBinding(uint set, uint binding, TResource resource)
    {
        Set = set;
        Binding = binding;
        Resource = resource;
    }

    public uint Set { get; }
    public uint Binding { get; }
    public TResource Resource { get; }

    public bool Equals(ComputeDispatchBinding<TResource> other)
        => Set == other.Set && Binding == other.Binding && EqualityComparer<TResource>.Default.Equals(Resource, other.Resource);

    public override bool Equals(object? obj) => obj is ComputeDispatchBinding<TResource> other && Equals(other);
    public override int GetHashCode()
    {
        var resourceHash = Resource is null ? 0 : EqualityComparer<TResource>.Default.GetHashCode(Resource);
        return unchecked((int)(Set * 397u ^ Binding * 17u ^ (uint)resourceHash));
    }
    public static bool operator ==(ComputeDispatchBinding<TResource> left, ComputeDispatchBinding<TResource> right) => left.Equals(right);
    public static bool operator !=(ComputeDispatchBinding<TResource> left, ComputeDispatchBinding<TResource> right) => !left.Equals(right);
}

public sealed class ComputeDispatchRequest<TResource>
{
    public ComputeDispatchRequest(
        ShaderArtifact artifact,
        ComputeDispatchDimensions dimensions,
        IReadOnlyList<ComputeDispatchBinding<TResource>> bindings)
    {
        ArgumentGuard.NotNull(artifact, nameof(artifact));
        Artifact = artifact;
        if (artifact.Stage != ShaderStage.Compute)
        {
            throw new ArgumentException("The shader artifact must contain a compute stage.", nameof(artifact));
        }

        Dimensions = dimensions;
        ArgumentGuard.NotNull(bindings, nameof(bindings));
        Bindings = bindings;

        var expected = new HashSet<(uint Set, uint Binding)>(artifact.Manifest.Resources
            .Select(resource => (resource.Set, resource.Binding)));
        var actual = bindings
            .Select(binding => (binding.Set, binding.Binding))
            .ToArray();

        if (actual.Distinct().Count() != actual.Length)
        {
            throw new ArgumentException("Compute dispatch bindings cannot contain duplicate set/binding pairs.", nameof(bindings));
        }

        if (actual.Length != expected.Count || actual.Any(binding => !expected.Contains(binding)))
        {
            throw new ArgumentException("Compute dispatch bindings must exactly match the artifact resource set/binding contract.", nameof(bindings));
        }
    }

    public ShaderArtifact Artifact { get; }

    public ComputeDispatchDimensions Dimensions { get; }

    public IReadOnlyList<ComputeDispatchBinding<TResource>> Bindings { get; }
}

public interface IComputeDispatcher<TResource>
{
    Task DispatchAsync(
        ComputeDispatchRequest<TResource> request,
        CancellationToken cancellationToken = default);
}
