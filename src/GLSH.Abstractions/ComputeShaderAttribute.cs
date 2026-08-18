using System;

namespace DVG.Shaders.Abstractions;

/// <summary>
/// Marks a static C# method as a compute shader entry point.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ComputeShaderAttribute : Attribute
{
    public uint LocalSizeX { get; }
    public uint LocalSizeY { get; }
    public uint LocalSizeZ { get; }

    public string? EntryPointName { get; }

    public ComputeShaderAttribute(uint localSizeX = 1, uint localSizeY = 1, uint localSizeZ = 1, string? entryPointName = null)
    {
        if (localSizeX == 0 || localSizeY == 0 || localSizeZ == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(localSizeX), "Compute local sizes must be positive.");
        }

        LocalSizeX = localSizeX;
        LocalSizeY = localSizeY;
        LocalSizeZ = localSizeZ;
        EntryPointName = entryPointName;
    }
}

