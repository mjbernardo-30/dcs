using LocalSonar.Cli;
using LocalSonar.Configuration;
using LocalSonar.Core;
using LocalSonar.Infrastructure;
using LocalSonar.Solutions;

namespace LocalSonar.Modes;

/// <summary>
/// SpecificSln: argument 2 is a .sln/.slnx file, or a folder that contains exactly one.
/// </summary>
public sealed class SpecificSolutionMode : IScanMode
{
    public ScanMode Mode => ScanMode.SpecificSln;

    public Task<ScanTarget> ResolveTargetAsync(
        CommandLineOptions options,
        ScannerSettings settings,
        CancellationToken cancellationToken)
    {
        var solutionPath = ResolveSolutionPath(options.SourcePath);
        var projects = SolutionReader.ReadProjects(solutionPath);

        if (projects.Count == 0)
        {
            throw new ScanException($"No C# projects (.csproj) found in '{solutionPath}'.");
        }

        var testCount = projects.Count(p => p.IsTestProject);
        ConsoleWriter.Info(
            $"Solution: {Path.GetFileName(solutionPath)} - {projects.Count} C# project(s)" +
            (testCount > 0 ? $", {testCount} detected as test project(s)" : "") +
            $", {projects.Sum(p => p.SourceFiles.Count)} .cs file(s).");

        var target = new ScanTarget
        {
            DisplayName = Path.GetFileNameWithoutExtension(solutionPath),
            RootDirectory = Path.GetDirectoryName(solutionPath)!,
            BuildEntryPoints = [solutionPath],
            Projects = projects,
        };

        return Task.FromResult(target);
    }

    private static string ResolveSolutionPath(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            if (!SolutionReader.IsSolutionFile(sourcePath))
            {
                throw new ScanException($"'{sourcePath}' is not a .sln or .slnx file.", ExitCode.InvalidArguments);
            }

            return sourcePath;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new ScanException($"Source path not found: '{sourcePath}'.", ExitCode.InvalidArguments);
        }

        var solutions = Directory.EnumerateFiles(sourcePath)
            .Where(SolutionReader.IsSolutionFile)
            .ToList();

        return solutions.Count switch
        {
            1 => solutions[0],
            0 => throw new ScanException(
                $"No .sln/.slnx file in '{sourcePath}'. Pass the solution file path instead.",
                ExitCode.InvalidArguments),
            _ => throw new ScanException(
                $"More than one solution in '{sourcePath}': " +
                $"{string.Join(", ", solutions.Select(Path.GetFileName))}. Pass the one you want.",
                ExitCode.InvalidArguments),
        };
    }
}
