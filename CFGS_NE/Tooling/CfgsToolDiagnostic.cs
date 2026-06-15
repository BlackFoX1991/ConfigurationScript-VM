namespace CFGS_VM.Tooling;

internal enum CfgsToolDiagnosticSeverity
{
    Info = 1,
    Warning = 2,
    Error = 3
}

internal sealed record CfgsToolDiagnostic(
    string Code,
    CfgsToolDiagnosticSeverity Severity,
    string Message,
    string SourceFile,
    int Line,
    int Column,
    int SpanLength)
{
    public string SeverityName => Severity switch
    {
        CfgsToolDiagnosticSeverity.Error => "error",
        CfgsToolDiagnosticSeverity.Warning => "warning",
        _ => "info"
    };
}
