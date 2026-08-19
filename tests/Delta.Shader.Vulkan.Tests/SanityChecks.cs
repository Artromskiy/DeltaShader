using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Delta.Shader.Backend.Glsl;
using Delta.Shader.Compiler;
using Delta.Shader.Compiler.IR;
using Xunit;
using Xunit.Sdk;

namespace Delta.Shader.Vulkan.Tests;

public class SanityChecks
{
    [Fact]
    public void GlslEmitter_Output_Compiles_And_Validates_With_Glslang_When_Available()
    {
        var glslang = ToolPath("glslangValidator");
        var spirvVal = ToolPath("spirv-val");
        if (string.IsNullOrWhiteSpace(glslang) || string.IsNullOrWhiteSpace(spirvVal))
        {
            throw SkipException.ForSkip("Skip: glslangValidator and/or spirv-val is not installed in PATH.");
        }

        var module = new ShaderIrModule
        {
            EntryPointName = "Compute",
            LocalSizeX = 8,
            LocalSizeY = 1,
            LocalSizeZ = 1,
            Resources =
            [
                new ShaderIrResource
                {
                    Name = "input",
                    ParameterName = "input",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 0,
                    GlslType = "float",
                    ReadOnly = true
                },
                new ShaderIrResource
                {
                    Name = "output",
                    ParameterName = "output",
                    Category = "storage-buffer",
                    Set = 0,
                    Binding = 1,
                    GlslType = "float",
                    ReadOnly = false
                }
            ],
            Requirements = ["Vulkan 1.2", "GLSL 460", "SPIRV 1.5"]
        };

        var emit = GlslEmitter.EmitFromModule(module);
        Assert.True(emit.Success);
        Assert.Contains("void main()", emit.Source);

        var workspace = Path.Combine(Path.GetTempPath(), "delta-shader-vulkan-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var glslFile = Path.Combine(workspace, "shader.glsl");
        var spvFile = Path.Combine(workspace, "shader.spv");
        File.WriteAllText(glslFile, emit.Source);

        var glslCompile = RunTool(glslang, $"-V --target-env vulkan1.2 -S comp {EscapePath(glslFile)} -o {EscapePath(spvFile)}");
        Assert.True(glslCompile.ExitCode == 0, $"glslang failed: {glslCompile.Output}");

        var validation = RunTool(spirvVal, $"--target-env vulkan1.2 {EscapePath(spvFile)}");
        Assert.True(validation.ExitCode == 0, $"spirv-val failed: {validation.Output}");
    }

    private static string? ToolPath(string toolName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separators = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ';' }
            : new[] { ':' };

        foreach (var part in pathEnv.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(part, toolName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exeCandidate = candidate + ".exe";
                if (File.Exists(exeCandidate))
                {
                    return exeCandidate;
                }
            }
        }

        return null;
    }

    private static (int ExitCode, string Output) RunTool(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var output = new StringBuilder();
        process.Start();
        output.AppendLine(process.StandardOutput.ReadToEnd());
        output.AppendLine(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    private static string EscapePath(string value) => $"\"{value}\"";
}
