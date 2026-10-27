using LocalSonar.Core;
using LocalSonar.Infrastructure;

namespace LocalSonar.Reporting;

public sealed class ConsoleReportWriter : IReportWriter
{
    private const int MaxIssuesPerSeverity = 25;
    private const int MaxDuplicatedFiles = 10;

    public Task WriteAsync(ScanResult result, CancellationToken cancellationToken)
    {
        if (result.IssuesAnalyzed)
        {
            ConsoleWriter.Header("Issues");
            foreach (var severity in Enum.GetValues<Severity>())
            {
                var count = result.CountBySeverity(severity);
                ConsoleWriter.Write($"  {severity,-9} {count,5}", count > 0 ? ColorFor(severity) : ConsoleColor.DarkGray);
            }

            foreach (var severity in new[] { Severity.Blocker, Severity.Critical, Severity.Major, Severity.Unknown })
            {
                var issues = result.Issues.Where(i => i.Severity == severity).ToList();
                if (issues.Count == 0) continue;

                Console.WriteLine();
                ConsoleWriter.Write($"  {severity}:", ColorFor(severity));
                foreach (var issue in issues.Take(MaxIssuesPerSeverity))
                {
                    ConsoleWriter.Info($"    {issue.RuleId,-7} {issue.RelativePath}({issue.Line},{issue.Column})  {issue.Message}");
                }

                if (issues.Count > MaxIssuesPerSeverity)
                {
                    ConsoleWriter.Muted($"    ... and {issues.Count - MaxIssuesPerSeverity} more (see report.html)");
                }
            }
        }

        if (result.Duplication is { } duplication)
        {
            ConsoleWriter.Header("Duplication");
            ConsoleWriter.Info(
                $"  {duplication.DuplicatedLinesPercent:F1}% duplicated lines " +
                $"({duplication.DuplicatedLines:N0} / {duplication.TotalLines:N0}), {duplication.DuplicatedBlocks} block(s)");

            foreach (var file in duplication.Files
                         .Where(f => f.DuplicatedLines > 0)
                         .OrderByDescending(f => f.DuplicatedLines)
                         .Take(MaxDuplicatedFiles))
            {
                var first = file.Blocks[0];
                var with = first.DuplicateOfFile is null ? "" : $"  e.g. lines {first.StartLine}-{first.EndLine} ~ {first.DuplicateOfFile}:{first.DuplicateOfLine}";
                ConsoleWriter.Info($"    {file.DuplicatedLines,5} lines  {file.RelativePath}{with}");
            }
        }

        foreach (var warning in result.Warnings.Distinct())
        {
            ConsoleWriter.Warn(warning);
        }

        if (result.QualityGate is { } gate)
        {
            ConsoleWriter.Header("Quality gate");
            foreach (var condition in gate.Conditions)
            {
                ConsoleWriter.Write(
                    $"  [{(condition.Passed ? "OK" : "FAIL")}] {condition.Metric,-22} {condition.Actual,10}   (required {condition.Threshold})",
                    condition.Passed ? ConsoleColor.Green : ConsoleColor.Red);
            }

            Console.WriteLine();
            if (result.Errors.Count > 0)
            {
                ConsoleWriter.Error("Scan did not complete cleanly: " + string.Join(" | ", result.Errors));
            }
            else if (gate.Passed)
            {
                ConsoleWriter.Success("QUALITY GATE PASSED - good to push.");
            }
            else
            {
                ConsoleWriter.Write("QUALITY GATE FAILED - fix the items above before pushing.", ConsoleColor.Red);
            }
        }

        foreach (var file in result.ReportFiles)
        {
            ConsoleWriter.Muted($"Report: {file}");
        }

        ConsoleWriter.Muted($"Total time: {result.Duration.TotalSeconds:F1}s");
        return Task.CompletedTask;
    }

    private static ConsoleColor ColorFor(Severity severity) => severity switch
    {
        Severity.Blocker or Severity.Critical => ConsoleColor.Red,
        Severity.Major => ConsoleColor.Yellow,
        Severity.Unknown => ConsoleColor.Magenta,
        _ => ConsoleColor.Gray,
    };
}
