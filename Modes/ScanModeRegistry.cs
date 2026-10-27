using LocalSonar.Cli;
using LocalSonar.Core;

namespace LocalSonar.Modes;

public sealed class ScanModeRegistry
{
    private readonly Dictionary<ScanMode, IScanMode> _modes = [];

    /// <summary>
    /// Register modes here. To implement a planned mode, replace its PlannedScanMode
    /// with the real class.
    /// </summary>
    public static ScanModeRegistry CreateDefault() => new ScanModeRegistry()
        .Register(new SpecificSolutionMode())
        .Register(new PlannedScanMode(ScanMode.SpecificFile,
            "Resolve the owning .sln, then set ScanTarget.FocusFiles to the one file."))
        .Register(new PlannedScanMode(ScanMode.Compare,
            "Run 'git diff <base>...HEAD -U0', then set ScanTarget.ChangedLines."))
        .Register(new PlannedScanMode(ScanMode.Full,
            "Find every .sln/.slnx under the folder and add each to BuildEntryPoints."));

    public ScanModeRegistry Register(IScanMode mode)
    {
        _modes[mode.Mode] = mode;
        return this;
    }

    public IScanMode Get(ScanMode mode) =>
        _modes.TryGetValue(mode, out var scanMode)
            ? scanMode
            : throw new ScanException($"No scan mode registered for '{mode}'.", ExitCode.NotImplemented);
}
