using System;
using Delta.Shader.Compiler.IR;
using Delta.Shader.Abstractions;
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

        return entries.GroupBy(entry => entry.Stage)
            .Select(group => group.Key == ShaderStage.Compute
                ? ComputeEntryPoints.ValidateAndBuild(context, frontend, options)
                : GraphicsEntryPoints.ValidateAndBuild(context, frontend, group.Key, options))
            .ToArray();
    }
}
