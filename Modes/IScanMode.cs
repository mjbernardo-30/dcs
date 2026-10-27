using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Core;

namespace LocalSonar.Modes;

/// <summary>
/// A scan mode turns argument 2 (the source path) into a ScanTarget.
/// Everything after that (build, rules, duplication, gate, reports) is shared.
/// </summary>
public interface IScanMode
{
    ScanMode Mode { get; }

    Task<ScanTarget> ResolveTargetAsync(
        CommandLineOptions options,
        ScannerSettings settings,
        CancellationToken cancellationToken);
}
