using System.Text.Json;
using LocalSonar.Core;

namespace LocalSonar.Configuration;

/// <summary>Bound from appsettings.json (or the file passed with --config).</summary>
public sealed class ScannerSettings
{
    public QualityGateSettings QualityGate { get; set; } = new();
    public IssueAnalysisSettings Issues { get; set; } = new();
    public DuplicationSettings Duplication { get; set; } = new();
    public SonarQubeSettings SonarQube { get; set; } = new();

    /// <summary>Report root folder. Empty = "reports" next to the exe (keeps the scanned repo clean).</summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>
    /// SonarScanner for .NET treats test projects as test code: they don't count toward
    /// duplication and get a different rule set. True mirrors that.
    /// </summary>
    public bool ExcludeTestProjects { get; set; } = true;

    /// <summary>Folder of the settings file; relative paths in settings resolve against it.</summary>
    public string BaseDirectory { get; private set; } = AppContext.BaseDirectory;

    public string ResolvePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(Path.Combine(BaseDirectory, path));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScannerSettings Load(string? path)
    {
        var explicitPath = path is not null;
        path ??= Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (!File.Exists(path))
        {
            if (explicitPath)
            {
                throw new ScanException($"Settings file not found: '{path}'.", ExitCode.InvalidArguments);
            }

            return new ScannerSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ScannerSettings>(File.ReadAllText(path), JsonOptions)
                           ?? new ScannerSettings();
            settings.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return settings;
        }
        catch (JsonException ex)
        {
            throw new ScanException($"Invalid settings file '{path}': {ex.Message}", ExitCode.InvalidArguments);
        }
    }
}

public sealed class QualityGateSettings
{
    public int MaxBlockerIssues { get; set; } = 0;
    public int MaxCriticalIssues { get; set; } = 0;
    public int MaxMajorIssues { get; set; } = 0;

    /// <summary>Fails when duplication is greater than this (same as a Sonar gate condition).</summary>
    public double MaxDuplicatedLinesPercent { get; set; } = 3.0;

    /// <summary>Treat issues whose severity is unknown (rule not in rules file) as failures.</summary>
    public bool FailOnUnknownSeverity { get; set; } = false;
}

public sealed class IssueAnalysisSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>SonarAnalyzer.CSharp NuGet version to inject. Match your SonarQube server's analyzer if you can.</summary>
    public string SonarAnalyzerVersion { get; set; } = "10.*";

    /// <summary>
    /// Offline option: folder containing SonarAnalyzer.CSharp.dll (and its sibling dlls,
    /// i.e. the package's analyzers/dotnet/cs folder). When set, no NuGet download is needed.
    /// </summary>
    public string SonarAnalyzerDirectory { get; set; } = "";

    /// <summary>Rule keys + severities from your quality profile. See README.</summary>
    public string RulesFile { get; set; } = "rules.json";

    public string BuildConfiguration { get; set; } = "Debug";

    /// <summary>Also report non-Sonar diagnostics (CAxxxx, IDExxxx, CSxxxx warnings).</summary>
    public bool IncludeNonSonarRules { get; set; } = false;

    /// <summary>Glob patterns relative to the solution folder, like sonar.exclusions.</summary>
    public List<string> Exclusions { get; set; } = [];

    /// <summary>Extra arguments appended to "dotnet build" (e.g. "-p:Platform=x64").</summary>
    public List<string> AdditionalBuildArguments { get; set; } = [];
}

public sealed class DuplicationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Sonar C#: a duplicate is at least 100 identical tokens...</summary>
    public int MinimumTokens { get; set; } = 100;

    /// <summary>...spread over at least 10 lines.</summary>
    public int MinimumLines { get; set; } = 10;

    /// <summary>Glob patterns relative to the solution folder, like sonar.cpd.exclusions.</summary>
    public List<string> Exclusions { get; set; } = [];
}

public sealed class SonarQubeSettings
{
    /// <summary>e.g. https://sonarqube.mybank.com - leave empty to use rules.json only.</summary>
    public string Url { get; set; } = "";

    /// <summary>Quality profile key (Quality Profiles page -> your C# profile -> key in the URL).</summary>
    public string QualityProfileKey { get; set; } = "";

    /// <summary>Environment variable holding a SonarQube user token (never put the token in this file).</summary>
    public string TokenEnvironmentVariable { get; set; } = "LOCALSONAR_TOKEN";

    /// <summary>Download rules on every run instead of only when rules.json is missing.</summary>
    public bool RefreshRulesOnEachRun { get; set; } = false;
}
