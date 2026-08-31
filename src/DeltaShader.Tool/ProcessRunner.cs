using System.Diagnostics;

namespace Delta.Shader.Tool;

internal readonly record struct ProcessResult(int ExitCode, string Output);

internal static class ProcessRunner
{
    public static ProcessResult Run(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        return Execute(process);
    }

    public static ProcessResult Run(string? fileName, params string[] arguments)
    {
        if (fileName is null)
        {
            return new ProcessResult(1, "External validation tool is not available.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return Execute(process);
    }

    private static ProcessResult Execute(Process process)
    {
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);
        return new ProcessResult(
            process.ExitCode,
            standardOutput.Result + standardError.Result + Environment.NewLine);
    }
}
