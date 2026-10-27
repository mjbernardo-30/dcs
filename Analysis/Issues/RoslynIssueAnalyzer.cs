using System.Text.RegularExpressions;
using LocalSonar.Core;
using LocalSonar.Infrastructure;

namespace LocalSonar.Analysis.Issues;

/// <summary>
/// Builds the target with SonarAnalyzer.CSharp injected (same Roslyn rules SonarScanner for .NET runs
/// in the pipeline), collects the SARIF error logs and maps each finding to its Sonar severity.
/// </summary>
public sealed partial class RoslynIssueAnalyzer : IAnalyzer
{
    public string Name => "Sonar rules (build + analyzers)";

    public bool IsEnabled(ScanContext context) =>
        context.Settings.Issues.Enabled && !context.Options.SkipIssues;

    public async Task AnalyzeAsync(ScanContext context, ScanResult result, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var issueSettings = settings.Issues;

        var targetsFile = Path.Combine(AppContext.BaseDirectory, "MsBuild", "LocalSonar.targets");
        if (!File.Exists(targetsFile))
        {
            throw new ScanException($"Missing {targetsFile}. Re-publish LocalSonar so the MsBuild folder is copied.");
        }

        var catalog = await RuleCatalogProvider.GetAsync(settings, cancellationToken);
        result.RuleCatalogLoaded = catalog.IsLoaded;
        if (!catalog.IsLoaded)
        {
            const string warning =
                "No rules file loaded, so Sonar severities are unknown and only default 'Sonar way' rules run. " +
                "Export your quality profile to rules.json (see README) for an accurate gate.";
            ConsoleWriter.Warn(warning);
            result.Warnings.Add(warning);
        }

        var sarifDirectory = Path.Combine(context.OutputDirectory, "sarif");
        Directory.CreateDirectory(sarifDirectory);

        string? globalConfig = null;
        if (catalog.IsLoaded)
        {
            globalConfig = Path.Combine(context.OutputDirectory, "localsonar.globalconfig");
            catalog.WriteGlobalConfig(globalConfig);
        }

        var analyzerDirectory = settings.ResolvePath(issueSettings.SonarAnalyzerDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (analyzerDirectory.Length > 0 && !Directory.Exists(analyzerDirectory))
        {
            throw new ScanException($"SonarAnalyzerDirectory not found: '{analyzerDirectory}'.");
        }

        result.BuildLogPath = Path.Combine(context.OutputDirectory, "build.log");
        result.IssuesAnalyzed = true;

        foreach (var entryPoint in context.Target.BuildEntryPoints)
        {
            var arguments = BuildArguments(entryPoint, targetsFile, sarifDirectory, analyzerDirectory, globalConfig, context);
            var succeeded = await RunBuildAsync(entryPoint, arguments, result.BuildLogPath, cancellationToken);
            if (!succeeded)
            {
                result.BuildSucceeded = false;
                result.Errors.Add($"Build failed for {Path.GetFileName(entryPoint)}. See {result.BuildLogPath}.");
            }
        }

        // Guard against a false pass: a "successful" build that compiled nothing reports zero issues.
        var expectsAnalysis = context.Target.Projects.Any(p => !p.IsTestProject || !settings.ExcludeTestProjects);
        if (result.BuildSucceeded && expectsAnalysis && !Directory.EnumerateFiles(sarifDirectory, "*.sarif").Any())
        {
            result.Errors.Add(
                "The build succeeded but no project was compiled with the analyzers, so issues could not be checked. " +
                $"Check that the solution builds its projects (configuration '{issueSettings.BuildConfiguration}'). See {result.BuildLogPath}.");
        }

        CollectIssues(context, result, catalog, sarifDirectory);

        ConsoleWriter.Info($"Found {result.Issues.Count} issue(s) in scope.");
    }

    private static List<string> BuildArguments(
        string entryPoint,
        string targetsFile,
        string sarifDirectory,
        string analyzerDirectory,
        string? globalConfig,
        ScanContext context)
    {
        var issueSettings = context.Settings.Issues;
        var arguments = new List<string>
        {
            "build", entryPoint,
            "-c", issueSettings.BuildConfiguration,
            "--no-incremental",     // analyzers only run when compilation actually happens
            "-nologo",
            "-tl:off",
            "-v:minimal",
            $"-p:CustomAfterMicrosoftCommonTargets={targetsFile}",
            $"-p:LocalSonarOutDir={sarifDirectory}",
            $"-p:LocalSonarAnalyzerVersion={issueSettings.SonarAnalyzerVersion}",
            $"-p:LocalSonarIncludeTests={(context.Settings.ExcludeTestProjects ? "false" : "true")}",
        };

        if (analyzerDirectory.Length > 0)
        {
            arguments.Add($"-p:LocalSonarAnalyzerDir={analyzerDirectory}");
        }

        if (globalConfig is not null)
        {
            arguments.Add($"-p:LocalSonarGlobalConfig={globalConfig}");
        }

        arguments.AddRange(issueSettings.AdditionalBuildArguments);
        return arguments;
    }

    private static async Task<bool> RunBuildAsync(
        string entryPoint,
        List<string> arguments,
        string logPath,
        CancellationToken cancellationToken)
    {
        ConsoleWriter.Info($"Building {Path.GetFileName(entryPoint)} with Sonar analyzers (full rebuild, may take a few minutes)...");

        var errorLines = new List<string>();
        var sync = new Lock();

        await using var log = new StreamWriter(logPath, append: true);
        await log.WriteLineAsync($"> dotnet {string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

        var exitCode = await ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            Path.GetDirectoryName(entryPoint)!,
            line =>
            {
                lock (sync)
                {
                    log.WriteLine(line);
                    if (BuildErrorLine().IsMatch(line))
                    {
                        errorLines.Add(line.Trim());
                    }
                }
            },
            cancellationToken);

        if (exitCode == 0)
        {
            ConsoleWriter.Success("Build succeeded.");
            return true;
        }

        ConsoleWriter.Error($"Build failed (exit code {exitCode}). The pipeline would fail too. First errors:");
        foreach (var line in errorLines.Distinct().Take(15))
        {
            ConsoleWriter.Muted("  " + line);
        }

        return false;
    }

    private static void CollectIssues(ScanContext context, ScanResult result, RuleCatalog catalog, string sarifDirectory)
    {
        var target = context.Target;
        var exclusions = new GlobMatcher(context.Settings.Issues.Exclusions);
        var includeNonSonar = context.Settings.Issues.IncludeNonSonarRules;
        var seen = new HashSet<(string, string, int, int, string)>();
        var droppedNotInProfile = 0;

        foreach (var diagnostic in SarifReader.ReadDirectory(sarifDirectory, w => result.Warnings.Add(w)))
        {
            var isSonarRule = SonarRuleId().IsMatch(diagnostic.RuleId);
            var rule = catalog.Find(diagnostic.RuleId);

            if (!isSonarRule && !includeNonSonar && rule is null)
            {
                continue;
            }

            // With a profile loaded, a Sonar rule that is not in it would not be raised by the pipeline.
            if (isSonarRule && catalog.IsLoaded && rule is null)
            {
                droppedNotInProfile++;
                continue;
            }

            var path = diagnostic.FilePath;
            var relativePath = target.ToRelativePath(path);

            // Generated files (obj/, *.g.cs) and files outside the solution folder.
            if (relativePath.StartsWith("..", StringComparison.Ordinal)
                || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || exclusions.IsMatch(relativePath)
                || !target.IsInScope(path, diagnostic.Line))
            {
                continue;
            }

            var project = target.FindOwningProject(path);
            if (project is { IsTestProject: true } && context.Settings.ExcludeTestProjects)
            {
                continue;
            }

            // The same file can be compiled more than once (multi-targeting, linked files).
            if (!seen.Add((diagnostic.RuleId, path.ToUpperInvariant(), diagnostic.Line, diagnostic.Column, diagnostic.Message)))
            {
                continue;
            }

            var severity = rule?.Severity ?? (isSonarRule ? Severity.Unknown : MapNonSonarLevel(diagnostic.Level));

            result.Issues.Add(new Issue(
                diagnostic.RuleId,
                severity,
                diagnostic.Message,
                path,
                relativePath,
                diagnostic.Line,
                diagnostic.Column,
                project?.Name,
                rule?.Name ?? diagnostic.RuleName,
                diagnostic.HelpUri ?? (isSonarRule ? $"https://rules.sonarsource.com/csharp/RSPEC-{diagnostic.RuleId[1..]}" : null)));
        }

        if (droppedNotInProfile > 0)
        {
            ConsoleWriter.Muted($"Ignored {droppedNotInProfile} finding(s) from Sonar rules that are not in your quality profile.");
        }

        result.Issues.Sort((a, b) =>
        {
            var bySeverity = a.Severity.CompareTo(b.Severity);
            if (bySeverity != 0) return bySeverity;
            var byFile = string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
            return byFile != 0 ? byFile : a.Line.CompareTo(b.Line);
        });
    }

    /// <summary>Non-Sonar diagnostics (only reported when IncludeNonSonarRules is on). Informational.</summary>
    private static Severity MapNonSonarLevel(string level) => level switch
    {
        "error" => Severity.Major,
        "warning" => Severity.Minor,
        _ => Severity.Info,
    };

    [GeneratedRegex(@"^S\d+$")]
    private static partial Regex SonarRuleId();

    [GeneratedRegex(@"\berror\s+[A-Z]+\d+\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex BuildErrorLine();
}
