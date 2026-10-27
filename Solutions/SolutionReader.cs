using System.Text.RegularExpressions;
using System.Xml.Linq;
using LocalSonar.Core;

namespace LocalSonar.Solutions;

/// <summary>Reads C# projects from .sln (classic) and .slnx (XML, default in .NET 10) solutions.</summary>
public static partial class SolutionReader
{
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "TestResults", "packages",
    };

    public static bool IsSolutionFile(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ProjectInfo> ReadProjects(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        var relativePaths = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(solutionPath)
            : ReadSln(solutionPath);

        return relativePaths
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(Path.Combine(solutionDirectory, NormalizeSeparators(p))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Select(ReadProject)
            .ToList();
    }

    private static IEnumerable<string> ReadSln(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var match = SlnProjectLine().Match(line);
            if (match.Success)
            {
                yield return match.Groups["path"].Value;
            }
        }
    }

    private static IEnumerable<string> ReadSlnx(string path) =>
        XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => (string?)e.Attribute("Path"))
            .OfType<string>();

    private static ProjectInfo ReadProject(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var files = new List<string>();
        CollectSourceFiles(directory, projectPath, files);
        return new ProjectInfo(name, projectPath, directory, IsTestProject(projectPath, name), files);
    }

    /// <summary>
    /// SDK-style projects include every *.cs under their folder. Skip build output and
    /// sub-folders that belong to another (nested) project.
    /// </summary>
    private static void CollectSourceFiles(string directory, string ownerProject, List<string> files)
    {
        files.AddRange(Directory.EnumerateFiles(directory, "*.cs"));

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (IgnoredFolders.Contains(Path.GetFileName(sub)))
            {
                continue;
            }

            var hasOtherProject = Directory.EnumerateFiles(sub, "*.csproj")
                .Any(p => !string.Equals(p, ownerProject, StringComparison.OrdinalIgnoreCase));
            if (!hasOtherProject)
            {
                CollectSourceFiles(sub, ownerProject, files);
            }
        }
    }

    private static bool IsTestProject(string projectPath, string name)
    {
        string content;
        try
        {
            content = File.ReadAllText(projectPath);
        }
        catch (IOException)
        {
            content = "";
        }

        return content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
               || content.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)
               || TestProjectName().IsMatch(name);
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)"\s*,""")]
    private static partial Regex SlnProjectLine();

    [GeneratedRegex(@"[.\-_](Unit|Integration|Functional|Component|Acceptance)?Tests?$", RegexOptions.IgnoreCase)]
    private static partial Regex TestProjectName();
}
