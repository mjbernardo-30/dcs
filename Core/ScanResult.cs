using LocalSonar.Analysis.Duplication;
using LocalSonar.Analysis.Issues;
using LocalSonar.QualityGate;

namespace LocalSonar.Core;

public sealed class ScanResult
{
    public required ScanTarget Target { get; init; }
    public required string OutputDirectory { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public TimeSpan Duration { get; set; }

    // Issues (rules) step
    public bool IssuesAnalyzed { get; set; }
    public bool BuildSucceeded { get; set; } = true;
    public string? BuildLogPath { get; set; }
    public bool RuleCatalogLoaded { get; set; }
    public List<Issue> Issues { get; } = [];

    // Duplication step
    public DuplicationReport? Duplication { get; set; }

    public QualityGateResult? QualityGate { get; set; }

    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> ReportFiles { get; } = [];

    public int CountBySeverity(Severity severity) => Issues.Count(i => i.Severity == severity);

    public ExitCode ExitCode =>
        Errors.Count > 0 ? ExitCode.ScanError
        : QualityGate is { Passed: false } ? ExitCode.QualityGateFailed
        : ExitCode.Passed;
}
