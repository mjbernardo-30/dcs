using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Core;

namespace LocalSonar.Analysis;

public sealed record ScanContext(
    ScannerSettings Settings,
    CommandLineOptions Options,
    ScanTarget Target,
    string OutputDirectory);

/// <summary>
/// One check (rules, duplication, ...). To add a check (e.g. coverage), implement this
/// and add it in ScanPipeline.CreateDefault.
/// </summary>
public interface IAnalyzer
{
    string Name { get; }

    bool IsEnabled(ScanContext context);

    Task AnalyzeAsync(ScanContext context, ScanResult result, CancellationToken cancellationToken);
}
