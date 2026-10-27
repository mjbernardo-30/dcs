using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSonar.Core;

namespace LocalSonar.Reporting;

public sealed class JsonReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task WriteAsync(ScanResult result, CancellationToken cancellationToken)
    {
        var path = Path.Combine(result.OutputDirectory, "report.json");
        var duplication = result.Duplication;

        var payload = new
        {
            target = result.Target.DisplayName,
            root = result.Target.RootDirectory,
            startedAt = result.StartedAt,
            durationSeconds = Math.Round(result.Duration.TotalSeconds, 1),
            qualityGate = new { passed = result.QualityGate?.Passed, conditions = result.QualityGate?.Conditions },
            issues = new
            {
                analyzed = result.IssuesAnalyzed,
                buildSucceeded = result.BuildSucceeded,
                ruleCatalogLoaded = result.RuleCatalogLoaded,
                countsBySeverity = Enum.GetValues<Severity>().ToDictionary(s => s.ToString(), result.CountBySeverity),
                items = result.Issues,
            },
            duplication = duplication is null ? null : new
            {
                duplication.TotalLines,
                duplication.DuplicatedLines,
                duplicatedLinesPercent = Math.Round(duplication.DuplicatedLinesPercent, 2),
                duplication.DuplicatedBlocks,
                files = duplication.Files.Where(f => f.DuplicatedLines > 0).OrderByDescending(f => f.DuplicatedLines),
            },
            warnings = result.Warnings,
            errors = result.Errors,
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, Options, cancellationToken);
        result.ReportFiles.Add(path);
    }
}
