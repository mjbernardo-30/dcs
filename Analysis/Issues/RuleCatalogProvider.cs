using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LocalSonar.Configuration;
using LocalSonar.Infrastructure;

namespace LocalSonar.Analysis.Issues;

/// <summary>
/// Loads rules.json, downloading it from SonarQube first when a server is configured and
/// the file is missing (or RefreshRulesOnEachRun is on).
/// </summary>
public static class RuleCatalogProvider
{
    private const int PageSize = 500;

    public static async Task<RuleCatalog> GetAsync(ScannerSettings settings, CancellationToken cancellationToken)
    {
        var rulesPath = settings.ResolvePath(settings.Issues.RulesFile);
        var sonar = settings.SonarQube;
        var serverConfigured = !string.IsNullOrWhiteSpace(sonar.Url) && !string.IsNullOrWhiteSpace(sonar.QualityProfileKey);

        if (serverConfigured && (sonar.RefreshRulesOnEachRun || !File.Exists(rulesPath)))
        {
            try
            {
                var downloaded = await DownloadAsync(sonar, cancellationToken);
                if (downloaded.IsLoaded && !string.IsNullOrEmpty(rulesPath))
                {
                    downloaded.SaveSimpleFormat(rulesPath);
                    ConsoleWriter.Info($"Downloaded {downloaded.Count} active C# rules from SonarQube into {rulesPath}.");
                }

                return downloaded;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                ConsoleWriter.Warn($"Could not download rules from SonarQube ({ex.Message}). Falling back to {rulesPath}.");
            }
        }

        if (!string.IsNullOrEmpty(rulesPath) && File.Exists(rulesPath))
        {
            try
            {
                var catalog = RuleCatalog.LoadFromFile(rulesPath);
                ConsoleWriter.Info($"Loaded {catalog.Count} rules from {rulesPath}.");
                return catalog;
            }
            catch (JsonException ex)
            {
                ConsoleWriter.Warn($"Rules file {rulesPath} is not valid JSON: {ex.Message}");
            }
        }

        return RuleCatalog.Empty;
    }

    private static async Task<RuleCatalog> DownloadAsync(SonarQubeSettings sonar, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = new Uri(sonar.Url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };

        var token = Environment.GetEnvironmentVariable(sonar.TokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(token))
        {
            // SonarQube accepts a user token as the basic-auth user name with an empty password.
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(token + ":"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        var catalog = new RuleCatalog();
        var page = 1;
        int total;

        do
        {
            var url = $"api/rules/search?qprofile={Uri.EscapeDataString(sonar.QualityProfileKey)}" +
                      $"&activation=true&actives=true&f=name,severity,impacts&ps={PageSize}&p={page}";

            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            total = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            root.TryGetProperty("actives", out var actives);

            if (root.TryGetProperty("rules", out var rules))
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    catalog.AddFromJson(rule, actives.ValueKind == JsonValueKind.Object ? actives : null);
                }
            }

            page++;
        }
        while ((page - 1) * PageSize < total);

        return catalog;
    }
}
