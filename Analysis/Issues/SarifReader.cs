using System.Text.Json;

namespace LocalSonar.Analysis.Issues;

public sealed record SarifDiagnostic(
    string RuleId,
    string Level,
    string Message,
    string FilePath,
    int Line,
    int Column,
    string? RuleName,
    string? HelpUri);

/// <summary>Reads Roslyn SARIF 2.1 error logs (produced via the ErrorLog MSBuild property).</summary>
public static class SarifReader
{
    public static IEnumerable<SarifDiagnostic> ReadDirectory(string directory, Action<string> onWarning)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.sarif"))
        {
            List<SarifDiagnostic> diagnostics;
            try
            {
                diagnostics = ReadFile(file);
            }
            catch (JsonException ex)
            {
                // Happens when the compiler crashed mid-write; skip the file but say so.
                onWarning($"Skipped unreadable SARIF file {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var diagnostic in diagnostics)
            {
                yield return diagnostic;
            }
        }
    }

    public static List<SarifDiagnostic> ReadFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var results = new List<SarifDiagnostic>();

        if (!document.RootElement.TryGetProperty("runs", out var runs))
        {
            return results;
        }

        foreach (var run in runs.EnumerateArray())
        {
            var ruleMeta = ReadRuleMetadata(run);

            if (!run.TryGetProperty("results", out var runResults))
            {
                continue;
            }

            foreach (var result in runResults.EnumerateArray())
            {
                // #pragma warning disable / [SuppressMessage] -> Sonar ignores these too.
                if (result.TryGetProperty("suppressions", out var suppressions)
                    && suppressions.ValueKind == JsonValueKind.Array
                    && suppressions.GetArrayLength() > 0)
                {
                    continue;
                }

                var ruleId = GetString(result, "ruleId");
                if (ruleId is null)
                {
                    continue;
                }

                var level = GetString(result, "level") ?? "warning";
                var message = result.TryGetProperty("message", out var m) ? GetString(m, "text") ?? "" : "";

                string? filePath = null;
                int line = 0, column = 0;

                if (result.TryGetProperty("locations", out var locations)
                    && locations.ValueKind == JsonValueKind.Array
                    && locations.GetArrayLength() > 0
                    && locations[0].TryGetProperty("physicalLocation", out var physical))
                {
                    if (physical.TryGetProperty("artifactLocation", out var artifact))
                    {
                        filePath = ToLocalPath(GetString(artifact, "uri"));
                    }

                    if (physical.TryGetProperty("region", out var region))
                    {
                        line = GetInt(region, "startLine");
                        column = GetInt(region, "startColumn");
                    }
                }

                // Project-level diagnostics without a file are not reported by Sonar either.
                if (filePath is null)
                {
                    continue;
                }

                ruleMeta.TryGetValue(ruleId, out var meta);
                results.Add(new SarifDiagnostic(ruleId, level, message, filePath, line, column, meta.Name, meta.HelpUri));
            }
        }

        return results;
    }

    private static Dictionary<string, (string? Name, string? HelpUri)> ReadRuleMetadata(JsonElement run)
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);

        if (run.TryGetProperty("tool", out var tool)
            && tool.TryGetProperty("driver", out var driver)
            && driver.TryGetProperty("rules", out var rules))
        {
            foreach (var rule in rules.EnumerateArray())
            {
                var id = GetString(rule, "id");
                if (id is null) continue;

                var name = rule.TryGetProperty("shortDescription", out var sd) ? GetString(sd, "text") : null;
                map[id] = (name, GetString(rule, "helpUri"));
            }
        }

        return map;
    }

    private static string? ToLocalPath(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile
            ? Path.GetFullPath(parsed.LocalPath)
            : Path.GetFullPath(Uri.UnescapeDataString(uri));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var i) ? i : 0;
}
