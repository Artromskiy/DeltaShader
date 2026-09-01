using System;
using System.IO;
using Delta.Shader.Compiler;

namespace Delta.Shader.Tool;

internal static class SpirvOptimizer
{
    public static ProcessResult Run(
        string? executable,
        string profile,
        ShaderOptimizationMode optimization,
        string spirvPath)
    {
        if (optimization == ShaderOptimizationMode.None)
        {
            return new ProcessResult(0, string.Empty);
        }

        var optimizationFlag = optimization switch
        {
            ShaderOptimizationMode.Performance => "-O",
            ShaderOptimizationMode.Size => "-Os",
            _ => throw new ArgumentOutOfRangeException(nameof(optimization), optimization, null)
        };
        var optimizedPath = spirvPath + ".optimized";

        try
        {
            var result = ProcessRunner.Run(
                executable,
                $"--target-env={profile}",
                "--preserve-interface",
                optimizationFlag,
                spirvPath,
                "-o",
                optimizedPath);
            if (result.ExitCode != 0)
            {
                DeleteTemporaryOutput(optimizedPath);
                return result;
            }

            if (!File.Exists(optimizedPath))
            {
                return new ProcessResult(1, "spirv-opt completed without producing an output file.");
            }

            File.Move(optimizedPath, spirvPath, true);
            return result;
        }
        catch (Exception exception)
        {
            DeleteTemporaryOutput(optimizedPath);
            return new ProcessResult(1, exception.ToString());
        }
    }

    private static void DeleteTemporaryOutput(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
