using System.Globalization;
using LocalSonar.Configuration;
using LocalSonar.Core;

namespace LocalSonar.QualityGate;

public sealed record GateCondition(string Metric, string Actual, string Threshold, bool Passed);

public sealed record QualityGateResult(IReadOnlyList<GateCondition> Conditions)
{
    public bool Passed => Conditions.All(c => c.Passed);
}

/// <summary>Mirrors the company gate: no blocker/critical/major issues, duplication within the limit.</summary>
public sealed class QualityGateEvaluator
{
    public QualityGateResult Evaluate(ScanResult result, QualityGateSettings gate)
    {
        var conditions = new List<GateCondition>();

        if (result.IssuesAnalyzed)
        {
            conditions.Add(new GateCondition("Build", result.BuildSucceeded ? "succeeded" : "failed", "must succeed", result.BuildSucceeded));
            AddIssueCondition(conditions, result, Severity.Blocker, gate.MaxBlockerIssues);
            AddIssueCondition(conditions, result, Severity.Critical, gate.MaxCriticalIssues);
            AddIssueCondition(conditions, result, Severity.Major, gate.MaxMajorIssues);

            if (gate.FailOnUnknownSeverity)
            {
                AddIssueCondition(conditions, result, Severity.Unknown, 0);
            }
        }

        if (result.Duplication is { } duplication)
        {
            var percent = Math.Round(duplication.DuplicatedLinesPercent, 1);
            conditions.Add(new GateCondition(
                "Duplicated lines (%)",
                percent.ToString("F1", CultureInfo.InvariantCulture),
                "<= " + gate.MaxDuplicatedLinesPercent.ToString("F1", CultureInfo.InvariantCulture),
                percent <= gate.MaxDuplicatedLinesPercent));
        }

        return new QualityGateResult(conditions);
    }

    private static void AddIssueCondition(List<GateCondition> conditions, ScanResult result, Severity severity, int max)
    {
        var count = result.CountBySeverity(severity);
        conditions.Add(new GateCondition($"{severity} issues", count.ToString(CultureInfo.InvariantCulture), $"<= {max}", count <= max));
    }
}
