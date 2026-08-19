using Delta.Shader.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Delta.Shader.Compiler;

public static class ShaderCompiler
{
    public static ShaderCompilationResult Compile(
        Compilation compilation,
        ShaderCompilationOptions? options = null)
    {
        var context = new ModuleCompilationContext(compilation);
        var frontend = new RoslynFrontend(compilation);
        return ComputeEntryPoints.ValidateAndBuild(context, frontend, options);
    }
}
