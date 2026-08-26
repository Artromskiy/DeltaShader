using System.Collections.Generic;
using Delta.Shader.Abstractions;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler.Syntax;

public sealed class ShaderEntryPointSymbol
{
    public ShaderEntryPointSymbol(string name, IMethodSymbol methodSymbol, ShaderStage stage, uint localSizeX = 1, uint localSizeY = 1, uint localSizeZ = 1)
    {
        Name = name;
        Method = methodSymbol;
        Stage = stage;
        LocalSizeX = localSizeX;
        LocalSizeY = localSizeY;
        LocalSizeZ = localSizeZ;
    }

    public string Name { get; }
    public IMethodSymbol Method { get; }
    public ShaderStage Stage { get; }
    public uint LocalSizeX { get; }
    public uint LocalSizeY { get; }
    public uint LocalSizeZ { get; }

    public IReadOnlyList<Location> SourceLocations => Method.Locations;
}
