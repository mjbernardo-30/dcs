using LocalSonar.Core;

namespace LocalSonar.Reporting;

/// <summary>Add a writer (e.g. CSV, SARIF for VS) here and register it in ScanPipeline.CreateDefault.</summary>
public interface IReportWriter
{
    Task WriteAsync(ScanResult result, CancellationToken cancellationToken);
}
