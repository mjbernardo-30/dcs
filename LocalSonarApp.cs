using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Core;
using LocalSonar.Infrastructure;
using LocalSonar.Modes;

namespace LocalSonar;

public static class LocalSonarApp
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!CommandLineOptions.TryParse(args, out var options, out var error))
        {
            if (error is not null)
            {
                ConsoleWriter.Error(error);
                Console.WriteLine();
            }

            CommandLineOptions.PrintUsage();
            return (int)ExitCode.InvalidArguments;
        }

        try
        {
            var settings = ScannerSettings.Load(options.ConfigPath);
            var mode = ScanModeRegistry.CreateDefault().Get(options.Mode);

            ConsoleWriter.Header($"LocalSonar - {options.Mode}");
            ConsoleWriter.Muted($"Source: {options.SourcePath}");

            var target = await mode.ResolveTargetAsync(options, settings, cancellationToken);
            var pipeline = ScanPipeline.CreateDefault(settings, options);
            var result = await pipeline.RunAsync(target, cancellationToken);

            return (int)result.ExitCode;
        }
        catch (ScanException ex)
        {
            ConsoleWriter.Error(ex.Message);
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            ConsoleWriter.Warn("Scan cancelled.");
            return (int)ExitCode.Cancelled;
        }
    }
}
