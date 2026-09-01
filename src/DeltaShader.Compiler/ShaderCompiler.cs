using System;
using Delta.Shader.Compiler.IR;
using Delta.Shader;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

public static class ShaderCompiler
{
    public static ShaderCompilationResult Compile(
        Compilation compilation,
        ShaderCompilationOptions? options = null)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var context = new ModuleCompilationContext(compilation);
        var frontend = new RoslynFrontend(compilation);
        return ComputeEntryPoints.ValidateAndBuild(context, frontend, options);
    }

    public static IReadOnlyList<ShaderCompilationResult> CompileAll(
        Compilation compilation,
        ShaderCompilationOptions? options = null)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var context = new ModuleCompilationContext(compilation);
        var frontend = new RoslynFrontend(compilation);
        var entries = frontend.FindShaderEntryPoints();
        if (entries.Count == 0)
        {
            return [new ShaderCompilationResult(string.Empty, false,
                [new ShaderDiagnostic(ShaderDiagnosticId.DSH004, "No shader entry point found.", Severity: ShaderDiagnosticSeverity.Error)])];
        }

        var results = entries.Select(entry => entry.Stage == ShaderStage.Compute
                ? ComputeEntryPoints.ValidateAndBuild(context, frontend, options, entry.Method.Name, ShaderMethodIdentity.Get(entry.Method))
                : GraphicsEntryPoints.ValidateAndBuild(context, frontend, entry.Stage, options, entry.Method.Name, ShaderMethodIdentity.Get(entry.Method)))
            .ToArray();

        return GraphicsInterstageResolver.ResolvePairs(results, options ?? ShaderCompilationOptions.Default);
    }

    public static ShaderCompositeContextResolution ResolveCompositeContext(
        IReadOnlyList<ShaderCompilationResult> layers)
        => ShaderCompositeContextResolver.Resolve(layers);

    public static ShaderCompositeCompilationResult ComposeGraphics(
        IReadOnlyList<ShaderCompilationResult> vertexLayers,
        IReadOnlyList<ShaderCompilationResult> fragmentLayers)
        => ShaderCompositeCompiler.Compose(vertexLayers, fragmentLayers);
}
