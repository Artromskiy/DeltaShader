using System.Collections.Generic;
using Delta.Shader.Abstractions;
using Delta.Shader.Compiler.IR;

namespace Delta.Shader.Compiler;

public sealed class ShaderCompilationResult
{
    public ShaderCompilationResult(
        string entryPointName,
        bool success,
        IReadOnlyList<ShaderDiagnostic> diagnostics,
        ShaderIrModule? module = null,
        ShaderCompilationOptions? options = null)
    {
        EntryPointName = entryPointName;
        Success = success;
        Diagnostics = diagnostics;
        Module = module;
        Manifest = success && module is not null ? ShaderManifest.FromModule(module) : null;
        AbiManifest = Manifest?.ToAbiManifest(options ?? ShaderCompilationOptions.Default);
    }

    public string EntryPointName { get; }
    public bool Success { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
    public ShaderIrModule? Module { get; }
    public ShaderManifest? Manifest { get; }
    public ShaderAbiManifest? AbiManifest { get; }
}

public sealed record ShaderCompilationOptions
{
    public static readonly ShaderCompilationOptions Default = new();

    public string Profile { get; init; } = "vulkan1.2";
    public string Spirv { get; init; } = "1.5";
    public string Glsl { get; init; } = "460";
}
