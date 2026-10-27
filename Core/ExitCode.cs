namespace LocalSonar.Core;

public enum ExitCode
{
    Passed = 0,
    QualityGateFailed = 1,
    InvalidArguments = 2,
    ScanError = 3,
    NotImplemented = 4,
    Cancelled = 130,
}

/// <summary>An expected failure with a user-facing message (no stack trace printed).</summary>
public sealed class ScanException(string message, ExitCode exitCode = ExitCode.ScanError) : Exception(message)
{
    public ExitCode ExitCode { get; } = exitCode;
}
