using System.Globalization;
using System.Text;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

var options = ParseOptions(args);
if (options.IsUsage)
{
    PrintUsage();
    return 1;
}

return options.Command switch
{
    "check" => await ExecuteCheckAsync(options),
    "emit" => await ExecuteEmitAsync(options),
    "build" => await ExecuteEmitAsync(options),
    _ => throw new InvalidOperationException($"Unhandled command '{options.Command}'.")
};

static async Task<int> ExecuteCheckAsync(ProgramOptions options)
{
    var result = await CompileProjectAsync(options);

    Console.WriteLine($"Compute entry points: {(result.Success ? "valid" : "invalid")}");
    foreach (var diagnostic in result.Diagnostics)
    {
        var marker = diagnostic.Severity == ShaderDiagnosticSeverity.Error ? "error" : "warning";
        Console.WriteLine($"{marker} {diagnostic.Id} {diagnostic.Location}: {diagnostic.Message}");
    }

    return result.Success ? 0 : 1;
}

static async Task<int> ExecuteEmitAsync(ProgramOptions options)
{
    if (!string.Equals(options.Backend, "glsl", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Only backend 'glsl' is currently supported. Requested: '{options.Backend}'.");
        return 1;
    }

    var result = await CompileProjectAsync(options);
    if (!result.Success)
    {
        Console.WriteLine("Emit failed: compile diagnostics:");
        foreach (var diagnostic in result.Diagnostics)
        {
            var marker = diagnostic.Severity == ShaderDiagnosticSeverity.Error ? "error" : "warning";
            Console.WriteLine($"{marker} {diagnostic.Id} {diagnostic.Location}: {diagnostic.Message}");
        }

        return 1;
    }

    if (result.Module is null)
    {
        Console.WriteLine("No IR module produced.");
        return 1;
    }

    var outputDirectory = options.OutputDirectory ??
        Path.Combine(Path.GetDirectoryName(options.ProjectPath) ?? Environment.CurrentDirectory, "obj", "Delta.Shader");

    Directory.CreateDirectory(outputDirectory);
    var entryName = string.IsNullOrWhiteSpace(result.EntryPointName) ? "ComputeMain" : result.EntryPointName;
    var outputFile = Path.Combine(outputDirectory, $"{entryName}.glsl");

    var emitResult = GlslEmitter.EmitFromModule(result.Module);
    if (!emitResult.Success || string.IsNullOrWhiteSpace(emitResult.Source))
    {
        Console.WriteLine("GLSL emitter produced empty output.");
        return 1;
    }

    await File.WriteAllTextAsync(outputFile, emitResult.Source, new UTF8Encoding(false));
    Console.WriteLine($"Wrote {outputFile}");
    return 0;
}

static async Task<ShaderCompilationResult> CompileProjectAsync(ProgramOptions options)
{
    if (!MSBuildLocator.IsRegistered)
    {
        MSBuildLocator.RegisterDefaults();
    }

        using var workspace = MSBuildWorkspace.Create();
    var project = await workspace.OpenProjectAsync(options.ProjectPath);
    var compilation = await project.GetCompilationAsync();

    if (compilation is null)
    {
        return new ShaderCompilationResult(
            string.Empty,
            false,
            [new ShaderDiagnostic(ShaderDiagnosticId.DSH004, $"Unable to load compilation for project '{options.ProjectPath}'.")]);
    }

        return ShaderCompiler.Compile(compilation, options.CompilationOptions);
    }

static ProgramOptions ParseOptions(string[] args)
{
    if (args.Length == 0)
    {
        return new ProgramOptions("usage", string.Empty, true, ShaderCompilationOptions.Default, null, string.Empty);
    }

    var command = args[0].ToLowerInvariant();
    if (command is not "check" and not "emit" and not "build")
    {
        return new ProgramOptions(command, string.Empty, true, ShaderCompilationOptions.Default, null, string.Empty);
    }

    if (args.Length == 1)
    {
        return new ProgramOptions(command, string.Empty, true, ShaderCompilationOptions.Default, null, string.Empty);
    }

    var compilationOptions = new ShaderCompilationOptions
    {
        Profile = ShaderCompilationOptions.Default.Profile,
        Spirv = ShaderCompilationOptions.Default.Spirv,
        Glsl = ShaderCompilationOptions.Default.Glsl
    };

    string? outputDir = null;
    var backend = "glsl";
    var commandArgs = args.Skip(1).ToArray();
    var projectPath = string.Empty;

    for (var i = 0; i < commandArgs.Length; i++)
    {
        var arg = commandArgs[i];
        if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < commandArgs.Length)
        {
            compilationOptions = compilationOptions with { Profile = commandArgs[++i] };
            continue;
        }

        if (arg.Equals("--spirv", StringComparison.OrdinalIgnoreCase) && i + 1 < commandArgs.Length)
        {
            compilationOptions = compilationOptions with { Spirv = commandArgs[++i] };
            continue;
        }

        if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < commandArgs.Length)
        {
            backend = commandArgs[++i];
            continue;
        }

        if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < commandArgs.Length)
        {
            outputDir = commandArgs[++i];
            continue;
        }

        if (arg.Equals("--glsl", StringComparison.OrdinalIgnoreCase) && i + 1 < commandArgs.Length)
        {
            compilationOptions = compilationOptions with { Glsl = commandArgs[++i] };
            continue;
        }

        if (projectPath.Length == 0)
        {
            projectPath = ResolveProjectPath(arg);
        }
    }

    if (string.IsNullOrWhiteSpace(projectPath))
    {
        return new ProgramOptions(command, string.Empty, true, compilationOptions, outputDir, backend);
    }

    return new ProgramOptions(command, projectPath, false, compilationOptions, outputDir, backend);
}

static string ResolveProjectPath(string arg)
{
    var normalized = arg;
    if (!Path.IsPathRooted(normalized))
    {
        normalized = Path.GetFullPath(normalized, Environment.CurrentDirectory);
    }

    return normalized;
}

static void PrintUsage()
{
    Console.WriteLine("dotnet delta-shader <check|emit|build> <project>");
    Console.WriteLine("  --backend <glsl>      output backend (currently only glsl)");
    Console.WriteLine("  --profile <vulkan1.2|vulkan1.3>   target profile");
    Console.WriteLine("  --spirv <version>     target SPIR-V version");
    Console.WriteLine("  --glsl <version>      target GLSL version");
    Console.WriteLine("  --out <path>          output directory for emitted artifacts");
    Console.WriteLine();
    Console.WriteLine("Returns 0 on success, non-zero on validation failures.");
}

internal readonly record struct ProgramOptions(
    string Command,
    string ProjectPath,
    bool IsUsage,
    ShaderCompilationOptions CompilationOptions,
    string? OutputDirectory,
    string Backend);
