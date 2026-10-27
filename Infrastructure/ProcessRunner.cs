using System.Diagnostics;
using System.ComponentModel;
using LocalSonar.Core;

namespace LocalSonar.Infrastructure;

public static class ProcessRunner
{
    /// <summary>Runs a process, streaming each output line to <paramref name="onLine"/>. Returns the exit code.</summary>
    public static async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList quotes each argument correctly (paths with spaces, trailing backslashes).
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ScanException($"Could not start '{fileName}': {ex.Message}. Is the .NET SDK installed and on PATH?");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        process.WaitForExit(); // flush remaining output events
        return process.ExitCode;
    }
}
