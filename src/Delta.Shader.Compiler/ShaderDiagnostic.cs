using System;

namespace Delta.Shader.Compiler;

public sealed record ShaderDiagnostic(
    string Id,
    string Message,
    string? FilePath = null,
    int StartLine = 0,
    int StartColumn = 0,
    int? EndLine = null,
    int? EndColumn = null,
    ShaderDiagnosticSeverity Severity = ShaderDiagnosticSeverity.Error)
{
    public string Location =>
        string.IsNullOrEmpty(FilePath)
            ? "<source>"
            : $"{FilePath}({StartLine},{StartColumn})";
}

public enum ShaderDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
