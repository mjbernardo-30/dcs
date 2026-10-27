namespace LocalSonar.Core;

public sealed record ProjectInfo(
    string Name,
    string ProjectFilePath,
    string ProjectDirectory,
    bool IsTestProject,
    IReadOnlyList<string> SourceFiles);

/// <summary>
/// What to scan. Every scan mode's only job is to produce one of these; the analyzers
/// and reports work on it without caring which mode created it.
/// </summary>
public sealed class ScanTarget
{
    /// <summary>Short name used in report titles and the report folder name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Folder that relative paths in reports and exclusion globs are based on.</summary>
    public required string RootDirectory { get; init; }

    /// <summary>Solutions or projects passed to "dotnet build" for the rules step.</summary>
    public required IReadOnlyList<string> BuildEntryPoints { get; init; }

    public required IReadOnlyList<ProjectInfo> Projects { get; init; }

    /// <summary>
    /// When set, only issues on these lines count (full path -> 1-based line numbers).
    /// Null means all code is in scope. Reserved for a future "Compare" (new code only) mode.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>>? ChangedLines { get; init; }

    /// <summary>
    /// When set, only these files are reported on (the whole solution is still used as the
    /// duplication corpus). Reserved for a future "SpecificFile" mode.
    /// </summary>
    public IReadOnlySet<string>? FocusFiles { get; init; }

    public IEnumerable<string> AllSourceFiles =>
        Projects.SelectMany(p => p.SourceFiles).Distinct(StringComparer.OrdinalIgnoreCase);

    public string ToRelativePath(string fullPath) =>
        Path.GetRelativePath(RootDirectory, fullPath).Replace('\\', '/');

    /// <summary>The deepest project whose folder contains the file.</summary>
    public ProjectInfo? FindOwningProject(string fullPath) =>
        Projects
            .Where(p => fullPath.StartsWith(
                p.ProjectDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .MaxBy(p => p.ProjectDirectory.Length);

    public bool IsInScope(string fullPath, int line)
    {
        if (FocusFiles is not null && !FocusFiles.Contains(fullPath))
        {
            return false;
        }

        return ChangedLines is null
               || (ChangedLines.TryGetValue(fullPath, out var lines) && lines.Contains(line));
    }
}
