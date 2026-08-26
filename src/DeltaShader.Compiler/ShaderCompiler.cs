using System;
using DeltaShader.Compiler.IR;
using DeltaShader.Abstractions;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DeltaShader.Compiler;

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

        return entries.Select(entry => entry.Stage == ShaderStage.Compute
                ? ComputeEntryPoints.ValidateAndBuild(context, frontend, options, entry.Method.Name, entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : GraphicsEntryPoints.ValidateAndBuild(context, frontend, entry.Stage, options, entry.Method.Name, entry.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            .ToArray();
    }
}
