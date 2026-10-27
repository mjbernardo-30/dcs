using System.Diagnostics;
using LocalSonar.Analysis;
using LocalSonar.Analysis.Duplication;
using LocalSonar.Analysis.Issues;
using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Infrastructure;
using LocalSonar.QualityGate;
using LocalSonar.Reporting;

namespace LocalSonar.Core;

/// <summary>
/// Runs every enabled analyzer against a ScanTarget, evaluates the quality gate and writes reports.
/// Mode-agnostic: new modes only need to produce a ScanTarget.
/// </summary>
public sealed class ScanPipeline(
    IReadOnlyList<IAnalyzer> analyzers,
    QualityGateEvaluator qualityGate,
    IReadOnlyList<IReportWriter> reportWriters,
    ScannerSettings settings,
    CommandLineOptions options)
{
    public static ScanPipeline CreateDefault(ScannerSettings settings, CommandLineOptions options) => new(
        analyzers: [new RoslynIssueAnalyzer(), new DuplicationAnalyzer()],
        qualityGate: new QualityGateEvaluator(),
        reportWriters: [new JsonReportWriter(), new HtmlReportWriter(), new ConsoleReportWriter()],
        settings,
        options);

    public async Task<ScanResult> RunAsync(ScanTarget target, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var outputDirectory = ResolveOutputDirectory(target);
        Directory.CreateDirectory(outputDirectory);

        var context = new ScanContext(settings, options, target, outputDirectory);
        var result = new ScanResult { Target = target, OutputDirectory = outputDirectory };

        foreach (var analyzer in analyzers)
        {
            if (!analyzer.IsEnabled(context))
            {
                ConsoleWriter.Muted($"Skipping {analyzer.Name}.");
                continue;
            }

            ConsoleWriter.Header(analyzer.Name);
            var step = Stopwatch.StartNew();
            try
            {
                await analyzer.AnalyzeAsync(context, result, cancellationToken);
                ConsoleWriter.Muted($"{analyzer.Name} finished in {step.Elapsed.TotalSeconds:F1}s.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ScanException ex)
            {
                result.Errors.Add($"{analyzer.Name}: {ex.Message}");
                ConsoleWriter.Error(ex.Message);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{analyzer.Name} crashed: {ex.Message}");
                ConsoleWriter.Error($"{analyzer.Name} crashed: {ex}");
            }
        }

        result.QualityGate = qualityGate.Evaluate(result, settings.QualityGate);
        result.Duration = stopwatch.Elapsed;

        foreach (var writer in reportWriters)
        {
            try
            {
                await writer.WriteAsync(result, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsoleWriter.Warn($"Could not write {writer.GetType().Name}: {ex.Message}");
            }
        }

        return result;
    }

    private string ResolveOutputDirectory(ScanTarget target)
    {
        var root = options.OutputDirectory
                   ?? (string.IsNullOrWhiteSpace(settings.OutputDirectory)
                       ? Path.Combine(AppContext.BaseDirectory, "reports")
                       : settings.OutputDirectory);

        var safeName = string.Concat(target.DisplayName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

        return Path.Combine(root, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}");
    }
}
