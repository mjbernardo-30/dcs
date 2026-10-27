using System.Collections.Concurrent;
using LocalSonar.Core;
using LocalSonar.Infrastructure;

namespace LocalSonar.Analysis.Duplication;

public sealed class DuplicationAnalyzer : IAnalyzer
{
    public string Name => "Duplication (copy-paste detection)";

    public bool IsEnabled(ScanContext context) =>
        context.Settings.Duplication.Enabled && !context.Options.SkipDuplication;

    public Task AnalyzeAsync(ScanContext context, ScanResult result, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var target = context.Target;
        var exclusions = new GlobMatcher(settings.Duplication.Exclusions);

        var testDirectories = settings.ExcludeTestProjects
            ? target.Projects.Where(p => p.IsTestProject).SelectMany(p => p.SourceFiles).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var candidates = target.AllSourceFiles
            .Where(f => !testDirectories.Contains(f))
            .Select(f => (Path: f, Relative: target.ToRelativePath(f)))
            .Where(f => !exclusions.IsMatch(f.Relative))
            .ToList();

        var tokenizer = new CpdTokenizer();
        var tokenized = new ConcurrentBag<TokenizedFile>();
        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(
            candidates,
            new ParallelOptions { CancellationToken = cancellationToken },
            file =>
            {
                try
                {
                    if (tokenizer.Tokenize(file.Path, file.Relative) is { } tokens)
                    {
                        tokenized.Add(tokens);
                    }
                }
                catch (IOException ex)
                {
                    failures.Add($"{file.Relative}: {ex.Message}");
                }
            });

        foreach (var failure in failures)
        {
            result.Warnings.Add($"Duplication: could not read {failure}");
        }

        // Stable order so results are repeatable.
        var files = tokenized.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        ConsoleWriter.Info($"Tokenized {files.Count} file(s) ({candidates.Count - files.Count} generated/unreadable skipped).");

        var report = DuplicationDetector.Detect(files, settings.Duplication.MinimumTokens, settings.Duplication.MinimumLines);

        // Future SpecificFile/Compare modes: the whole solution stays the corpus,
        // but only focused files are reported.
        if (target.FocusFiles is not null)
        {
            report = report with { Files = report.Files.Where(f => target.FocusFiles.Contains(f.FilePath)).ToList() };
        }

        result.Duplication = report;
        ConsoleWriter.Info(
            $"Duplicated lines: {report.DuplicatedLines:N0} of {report.TotalLines:N0} " +
            $"({report.DuplicatedLinesPercent:F1}%) in {report.DuplicatedBlocks} block(s).");

        return Task.CompletedTask;
    }
}
