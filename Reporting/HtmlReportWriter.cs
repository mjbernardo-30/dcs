using System.Net;
using System.Text;
using LocalSonar.Core;

namespace LocalSonar.Reporting;

/// <summary>Self-contained report.html (no external files), filterable by severity.</summary>
public sealed class HtmlReportWriter : IReportWriter
{
    public async Task WriteAsync(ScanResult result, CancellationToken cancellationToken)
    {
        var path = Path.Combine(result.OutputDirectory, "report.html");
        await File.WriteAllTextAsync(path, Render(result), Encoding.UTF8, cancellationToken);
        result.ReportFiles.Add(path);
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Render(ScanResult result)
    {
        var gate = result.QualityGate;
        var passed = gate?.Passed == true && result.Errors.Count == 0;
        var sb = new StringBuilder();

        sb.Append($$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>LocalSonar - {{{E(result.Target.DisplayName)}}}</title>
            <style>
            :root{--bg:#f6f7f9;--card:#fff;--text:#1d2330;--muted:#667085;--line:#e3e6eb;--ok:#127a3e;--bad:#c4321c;--warn:#a15c00;--accent:#2a5bd7}
            @media (prefers-color-scheme:dark){:root{--bg:#12151b;--card:#1b2029;--text:#e6e9ef;--muted:#9aa3b2;--line:#2c3340;--ok:#4cc27d;--bad:#ff7a66;--warn:#f0b450;--accent:#7ea2ff}}
            *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 "Segoe UI",system-ui,sans-serif}
            main{max-width:1200px;margin:0 auto;padding:24px 16px}
            h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 10px}
            .muted{color:var(--muted)}.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px}
            .banner{display:flex;align-items:center;gap:12px;font-weight:600;font-size:18px;margin:16px 0}
            .banner.ok{color:var(--ok)}.banner.bad{color:var(--bad)}
            .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px}
            .stat .n{font-size:26px;font-weight:600}.stat .l{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.04em}
            table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden}
            th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
            th{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
            td.path{font-family:Consolas,monospace;font-size:12.5px;word-break:break-all}
            .sev{font-weight:600;font-size:12px;padding:2px 8px;border-radius:999px;border:1px solid currentColor;white-space:nowrap}
            .Blocker,.Critical{color:var(--bad)}.Major{color:var(--warn)}.Minor,.Info{color:var(--muted)}.Unknown{color:var(--accent)}
            .pass{color:var(--ok);font-weight:600}.fail{color:var(--bad);font-weight:600}
            .filters{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:10px}
            .filters button{background:var(--card);color:var(--text);border:1px solid var(--line);border-radius:999px;padding:4px 12px;cursor:pointer;font:inherit}
            .filters button.on{border-color:var(--accent);color:var(--accent)}
            a{color:var(--accent)}ul{margin:6px 0;padding-left:20px}
            .scroll{overflow-x:auto}
            </style></head><body><main>
            <h1>{{{E(result.Target.DisplayName)}}}</h1>
            <div class="muted">{{{E(result.Target.RootDirectory)}}} &middot; {{{result.StartedAt:yyyy-MM-dd HH:mm}}} &middot; {{{result.Duration.TotalSeconds:F0}}}s</div>
            <div class="banner {{{(passed ? "ok" : "bad")}}}">{{{(passed ? "Quality gate passed" : result.Errors.Count > 0 ? "Scan incomplete" : "Quality gate failed")}}}</div>
            """);

        if (gate is not null)
        {
            sb.Append("<div class=\"scroll\"><table><tr><th>Condition</th><th>Actual</th><th>Required</th><th>Status</th></tr>");
            foreach (var c in gate.Conditions)
            {
                sb.Append($"<tr><td>{E(c.Metric)}</td><td>{E(c.Actual)}</td><td>{E(c.Threshold)}</td><td class=\"{(c.Passed ? "pass" : "fail")}\">{(c.Passed ? "OK" : "FAIL")}</td></tr>");
            }

            sb.Append("</table></div>");
        }

        if (result.Errors.Count + result.Warnings.Count > 0)
        {
            sb.Append("<h2>Messages</h2><div class=\"card\"><ul>");
            foreach (var e in result.Errors) sb.Append($"<li class=\"fail\">{E(e)}</li>");
            foreach (var w in result.Warnings.Distinct()) sb.Append($"<li>{E(w)}</li>");
            sb.Append("</ul></div>");
        }

        if (result.IssuesAnalyzed)
        {
            sb.Append("<h2>Issues</h2><div class=\"grid\">");
            foreach (var s in Enum.GetValues<Severity>())
            {
                sb.Append($"<div class=\"card stat\"><div class=\"n {s}\">{result.CountBySeverity(s)}</div><div class=\"l\">{s}</div></div>");
            }

            sb.Append("</div>");

            if (result.Issues.Count > 0)
            {
                sb.Append("<div style=\"margin-top:14px\" class=\"filters\" id=\"filters\"><button class=\"on\" data-s=\"all\">All</button>");
                foreach (var s in Enum.GetValues<Severity>().Where(s => result.CountBySeverity(s) > 0))
                {
                    sb.Append($"<button data-s=\"{s}\">{s} ({result.CountBySeverity(s)})</button>");
                }

                sb.Append("</div><div class=\"scroll\"><table id=\"issues\"><tr><th>Severity</th><th>Rule</th><th>Location</th><th>Message</th></tr>");
                foreach (var i in result.Issues)
                {
                    var rule = i.HelpUri is null ? E(i.RuleId) : $"<a href=\"{E(i.HelpUri)}\" target=\"_blank\" title=\"{E(i.RuleName)}\">{E(i.RuleId)}</a>";
                    sb.Append($"<tr data-s=\"{i.Severity}\"><td><span class=\"sev {i.Severity}\">{i.Severity}</span></td><td>{rule}</td><td class=\"path\">{E(i.RelativePath)}:{i.Line}</td><td>{E(i.Message)}</td></tr>");
                }

                sb.Append("</table></div>");
            }
        }

        if (result.Duplication is { } d)
        {
            sb.Append($$"""
                <h2>Duplication</h2>
                <div class="grid">
                <div class="card stat"><div class="n">{{d.DuplicatedLinesPercent:F1}}%</div><div class="l">Duplicated lines</div></div>
                <div class="card stat"><div class="n">{{d.DuplicatedLines:N0}}</div><div class="l">Duplicated lines</div></div>
                <div class="card stat"><div class="n">{{d.TotalLines:N0}}</div><div class="l">Total lines</div></div>
                <div class="card stat"><div class="n">{{d.DuplicatedBlocks}}</div><div class="l">Blocks</div></div>
                </div>
                """);

            var dupFiles = d.Files.Where(f => f.DuplicatedLines > 0).OrderByDescending(f => f.DuplicatedLines).ToList();
            if (dupFiles.Count > 0)
            {
                sb.Append("<div class=\"scroll\" style=\"margin-top:14px\"><table><tr><th>File</th><th>Dup. lines</th><th>%</th><th>Blocks (lines &rarr; duplicate of)</th></tr>");
                foreach (var f in dupFiles)
                {
                    var blocks = string.Join("<br>", f.Blocks.Select(b =>
                        $"{b.StartLine}-{b.EndLine}" + (b.DuplicateOfFile is null ? "" : $" &rarr; {E(b.DuplicateOfFile)}:{b.DuplicateOfLine}")));
                    sb.Append($"<tr><td class=\"path\">{E(f.RelativePath)}</td><td>{f.DuplicatedLines}</td><td>{f.DuplicatedLinesPercent:F1}</td><td class=\"path\">{blocks}</td></tr>");
                }

                sb.Append("</table></div>");
            }

            sb.Append($"<p class=\"muted\">Rule: at least {d.MinimumTokens} identical tokens over at least {d.MinimumLines} lines (Sonar C# defaults). Local figure is an estimate of the server's value.</p>");
        }

        sb.Append("""
            <script>
            (function(){var f=document.getElementById('filters');if(!f)return;
            f.addEventListener('click',function(e){var b=e.target.closest('button');if(!b)return;
            f.querySelectorAll('button').forEach(function(x){x.classList.toggle('on',x===b)});
            var s=b.getAttribute('data-s');
            document.querySelectorAll('#issues tr[data-s]').forEach(function(r){r.style.display=(s==='all'||r.getAttribute('data-s')===s)?'':'none'});});})();
            </script>
            </main></body></html>
            """);

        return sb.ToString();
    }
}
