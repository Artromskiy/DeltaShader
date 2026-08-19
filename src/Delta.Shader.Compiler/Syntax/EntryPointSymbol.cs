using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler.Syntax;

public sealed class ShaderEntryPointSymbol
{
    public ShaderEntryPointSymbol(string name, IMethodSymbol methodSymbol, uint localSizeX, uint localSizeY, uint localSizeZ)
    {
        Name = name;
        Method = methodSymbol;
        LocalSizeX = localSizeX;
        LocalSizeY = localSizeY;
        LocalSizeZ = localSizeZ;
    }

    public string Name { get; }
    public IMethodSymbol Method { get; }
    public uint LocalSizeX { get; }
    public uint LocalSizeY { get; }
    public uint LocalSizeZ { get; }

    public IReadOnlyList<Location> SourceLocations => Method.Locations;
}
