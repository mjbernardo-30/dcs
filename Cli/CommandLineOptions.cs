namespace LocalSonar.Cli;

/// <summary>
/// The first command-line argument. Add a value here, then register an IScanMode for it in ScanModeRegistry.
/// </summary>
public enum ScanMode
{
    SpecificSln,
    SpecificFile,
    Compare,
    Full,
}

/// <summary>
/// Parsed command line:  LocalSonar &lt;mode&gt; &lt;sourcePath&gt; [--options]
/// </summary>
public sealed class CommandLineOptions
{
    public required ScanMode Mode { get; init; }

    /// <summary>Argument 2. Absolute path (a .sln/.slnx file or a folder for SpecificSln).</summary>
    public required string SourcePath { get; init; }

    public string? ConfigPath { get; init; }
    public string? OutputDirectory { get; init; }
    public bool SkipIssues { get; init; }
    public bool SkipDuplication { get; init; }

    /// <summary>
    /// Any other "--name value" pairs, kept for future modes (e.g. Compare could read "--base develop").
    /// </summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string[] args, out CommandLineOptions options, out string? error)
    {
        options = null!;
        error = null;

        if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "/?"))
        {
            return false;
        }

        if (args.Length < 2)
        {
            error = "Expected 2 arguments: <mode> <sourcePath>.";
            return false;
        }

        if (!Enum.TryParse<ScanMode>(args[0], ignoreCase: true, out var mode) || !Enum.IsDefined(mode)
            || int.TryParse(args[0], out _))
        {
            error = $"Unknown mode '{args[0]}'. Valid modes: {string.Join(", ", Enum.GetNames<ScanMode>())}.";
            return false;
        }

        var sourcePath = args[1].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "Source path (argument 2) is empty.";
            return false;
        }

        string? configPath = null;
        string? outputDirectory = null;
        var skipIssues = false;
        var skipDuplication = false;
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 2; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{arg}'. Options must start with '--'.";
                return false;
            }

            var name = arg[2..].ToLowerInvariant();
            switch (name)
            {
                case "skip-issues":
                    skipIssues = true;
                    break;
                case "skip-duplication":
                    skipDuplication = true;
                    break;
                case "config":
                case "output":
                    if (i + 1 >= args.Length)
                    {
                        error = $"Option '--{name}' needs a value.";
                        return false;
                    }

                    var value = Path.GetFullPath(args[++i].Trim('"'));
                    if (name == "config") configPath = value; else outputDirectory = value;
                    break;
                default:
                    // Unknown options are kept for future modes; a following non-option token is its value.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        extra[name] = args[++i];
                    }
                    else
                    {
                        extra[name] = "true";
                    }

                    break;
            }
        }

        options = new CommandLineOptions
        {
            Mode = mode,
            SourcePath = Path.GetFullPath(sourcePath),
            ConfigPath = configPath,
            OutputDirectory = outputDirectory,
            SkipIssues = skipIssues,
            SkipDuplication = skipDuplication,
            Extra = extra,
        };
        return true;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            LocalSonar - run Sonar-style checks locally before the pipeline does.

            Usage:
              LocalSonar <mode> <sourcePath> [options]

            Modes:
              SpecificSln   Scan one solution. <sourcePath> = path to a .sln/.slnx file,
                            or a folder that contains exactly one solution.
              SpecificFile  (planned) Scan a single .cs file.
              Compare       (planned) Scan only code changed against a base branch.
              Full          (planned) Scan every solution under a folder.

            Options:
              --config <path>      Settings file (default: appsettings.json next to the exe).
              --output <folder>    Where reports are written.
              --skip-issues        Skip the build + Sonar rules step.
              --skip-duplication   Skip duplication detection.

            Exit codes:
              0 passed, 1 quality gate failed, 2 bad arguments, 3 scan error, 4 mode not implemented.

            Example:
              LocalSonar SpecificSln "C:\src\MyApp\MyApp.sln"
            """);
    }
}
