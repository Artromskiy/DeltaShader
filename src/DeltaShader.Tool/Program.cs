using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Delta.Shader.Abstractions;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.Tool;
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
    "CHECK" => await ExecuteCheckAsync(options).ConfigureAwait(false),
    "EMIT" => await ExecuteEmitAsync(options).ConfigureAwait(false),
    "BUILD" => await ExecuteEmitAsync(options).ConfigureAwait(false),
    _ => throw new InvalidOperationException($"Unhandled command '{options.Command}'.")
};

static async Task<int> ExecuteCheckAsync(ProgramOptions options)
{
    var results = await CompileProjectAsync(options).ConfigureAwait(false);
    var success = results.All(result => result.Success);
    Console.WriteLine($"Shader entry points: {(success ? "valid" : "invalid")}");
    foreach (var result in results)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            var marker = diagnostic.Severity == ShaderDiagnosticSeverity.Error ? "error" : "warning";
            Console.WriteLine($"{marker} {diagnostic.Id} {diagnostic.Location}: {diagnostic.Message}");
        }
    }

    return success ? 0 : 1;
}

static async Task<int> ExecuteEmitAsync(ProgramOptions options)
{
    if (!string.Equals(options.Backend, "glsl", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(options.Backend, "spirv", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Supported backends are 'glsl' and 'spirv'. Requested: '{options.Backend}'.");
        return 1;
    }

    var results = await CompileProjectAsync(options).ConfigureAwait(false);
    if (results.Any(result => !result.Success))
    {
        Console.WriteLine("Emit failed: compile diagnostics:");
        foreach (var result in results)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                var marker = diagnostic.Severity == ShaderDiagnosticSeverity.Error ? "error" : "warning";
                Console.WriteLine($"{marker} {diagnostic.Id} {diagnostic.Location}: {diagnostic.Message}");
            }
        }

        return 1;
    }

    var outputDirectory = options.OutputDirectory ??
        Path.Combine(Path.GetDirectoryName(options.ProjectPath) ?? Environment.CurrentDirectory, "obj", "DeltaShader");
    Directory.CreateDirectory(outputDirectory);
    foreach (var result in results)
    {
        var manifest = result.AbiManifest;
        if (result.Module is null || manifest is null)
        {
            return 1;
        }

        var entryName = string.IsNullOrWhiteSpace(result.SourceMethodName)
            ? result.Module.Stage.ToString()
            : result.SourceMethodName;
        var stageSuffix = result.Module.Stage switch
        {
            ShaderStage.Vertex => "vert",
            ShaderStage.Fragment => "frag",
            _ => "comp"
        };
        var fileStem = $"{entryName}.{stageSuffix}";
        var glslFile = Path.Combine(outputDirectory, $"{fileStem}.glsl");
        var manifestFile = Path.Combine(outputDirectory, $"{fileStem}.shader.json");
        var emitResult = GlslEmitter.EmitFromModule(result.Module);
        if (!emitResult.Success)
        {
            return 1;
        }

        await File.WriteAllTextAsync(glslFile, emitResult.Source, new UTF8Encoding(false)).ConfigureAwait(false);
        await File.WriteAllTextAsync(manifestFile, JsonSerializer.Serialize(result.AbiManifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false)).ConfigureAwait(false);
        if (string.Equals(options.Backend, "spirv", StringComparison.OrdinalIgnoreCase))
        {
            var glslang = ToolPath("glslangValidator");
            var spirvValidator = ToolPath("spirv-val");
            if (glslang is null || spirvValidator is null)
            {
                return 1;
            }

            var spirvFile = Path.Combine(outputDirectory, $"{fileStem}.spv");
            var compile = RunTool(glslang, $"-V --target-env {EscapeArgument(options.CompilationOptions.Profile)} -S {stageSuffix} {EscapeArgument(glslFile)} -o {EscapeArgument(spirvFile)}");
            if (compile.ExitCode != 0) { Console.WriteLine($"glslangValidator failed:{Environment.NewLine}{compile.Output}"); return 1; }
            var validation = RunTool(spirvValidator, $"--target-env {EscapeArgument(options.CompilationOptions.Profile)} {EscapeArgument(spirvFile)}");
            if (validation.ExitCode != 0) { Console.WriteLine($"spirv-val failed:{Environment.NewLine}{validation.Output}"); return 1; }
            var artifact = ShaderArtifactPublisher.Create(
                await File.ReadAllBytesAsync(spirvFile).ConfigureAwait(false),
                manifest);
            await File.WriteAllBytesAsync(spirvFile, artifact.CopySpirv()).ConfigureAwait(false);
            Console.WriteLine($"Wrote {spirvFile}");
        }
        Console.WriteLine($"Wrote {glslFile}");
        Console.WriteLine($"Wrote {manifestFile}");
    }
    return 0;
}

static async Task<IReadOnlyList<ShaderCompilationResult>> CompileProjectAsync(ProgramOptions options)
{
    if (!MSBuildLocator.IsRegistered)
    {
        MSBuildLocator.RegisterDefaults();
    }

    using var workspace = MSBuildWorkspace.Create();
    var project = await workspace.OpenProjectAsync(options.ProjectPath).ConfigureAwait(false);
    var compilation = await project.GetCompilationAsync().ConfigureAwait(false);

    if (compilation is null)
    {
        return [new ShaderCompilationResult(string.Empty, false, [new ShaderDiagnostic(ShaderDiagnosticId.DSH004, $"Unable to load compilation for project '{options.ProjectPath}'.")])];
    }

    return ShaderCompiler.CompileAll(compilation, options.CompilationOptions);
}

static ProgramOptions ParseOptions(string[] args)
{
    if (args.Length == 0)
    {
        return new ProgramOptions("usage", string.Empty, true, ShaderCompilationOptions.Default, null, string.Empty);
    }

    var command = args[0].ToUpperInvariant();
    if (command is not "CHECK" and not "EMIT" and not "BUILD")
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
    var backend = command == "BUILD" ? "spirv" : "glsl";
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
    Console.WriteLine("  --backend <glsl|spirv> output backend; spirv uses glslangValidator + spirv-val");
    Console.WriteLine("  --profile <vulkan1.2|vulkan1.3>   target profile");
    Console.WriteLine("  --spirv <version>     target SPIR-V version");
    Console.WriteLine("  --glsl <version>      target GLSL version");
    Console.WriteLine("  --out <path>          output directory for emitted artifacts");
    Console.WriteLine();
    Console.WriteLine("Returns 0 on success, non-zero on validation failures.");
}

static string? ToolPath(string toolName)
{
    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    var separators = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new[] { ';' } : new[] { ':' };
    foreach (var part in pathEnv.Split(separators, StringSplitOptions.RemoveEmptyEntries))
    {
        var candidate = Path.Combine(part, toolName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(candidate + ".exe"))
        {
            return candidate + ".exe";
        }
    }

    return null;
}

static (int ExitCode, string Output) RunTool(string fileName, string arguments)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    var output = new StringBuilder();
    process.Start();
    output.AppendLine(process.StandardOutput.ReadToEnd());
    output.AppendLine(process.StandardError.ReadToEnd());
    process.WaitForExit();
    return (process.ExitCode, output.ToString());
}

static string EscapeArgument(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

internal readonly record struct ProgramOptions(
    string Command,
    string ProjectPath,
    bool IsUsage,
    ShaderCompilationOptions CompilationOptions,
    string? OutputDirectory,
    string Backend);
