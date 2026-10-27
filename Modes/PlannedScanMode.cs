using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Core;

namespace LocalSonar.Modes;

/// <summary>Placeholder for modes that are accepted on the command line but not built yet.</summary>
public sealed class PlannedScanMode(ScanMode mode, string implementationHint) : IScanMode
{
    public ScanMode Mode { get; } = mode;

    public Task<ScanTarget> ResolveTargetAsync(
        CommandLineOptions options,
        ScannerSettings settings,
        CancellationToken cancellationToken) =>
        throw new ScanException(
            $"Mode '{Mode}' is not implemented yet. Use SpecificSln for now. (Plan: {implementationHint})",
            ExitCode.NotImplemented);
}
