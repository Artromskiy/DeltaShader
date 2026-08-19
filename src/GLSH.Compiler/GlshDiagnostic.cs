using System;

namespace Delta.Shader.Compiler;

public sealed record GlshDiagnostic(
    string Id,
    string Message,
    string? FilePath = null,
    int StartLine = 0,
    int StartColumn = 0,
    int? EndLine = null,
    int? EndColumn = null,
    GlshDiagnosticSeverity Severity = GlshDiagnosticSeverity.Error)
{
    public string Location =>
        string.IsNullOrEmpty(FilePath)
            ? "<source>"
            : $"{FilePath}({StartLine},{StartColumn})";
}

public enum GlshDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
