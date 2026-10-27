using System.Text;
using System.Text.RegularExpressions;

namespace LocalSonar.Infrastructure;

/// <summary>
/// Sonar-style glob patterns against '/'-separated relative paths:
/// "**/Migrations/**", "**/*.Designer.cs", "src/Legacy/*.cs".
/// </summary>
public sealed class GlobMatcher
{
    private readonly List<Regex> _patterns;

    public GlobMatcher(IEnumerable<string> patterns)
    {
        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(ToRegex)
            .ToList();
    }

    public bool IsMatch(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        return _patterns.Any(r => r.IsMatch(path));
    }

    private static Regex ToRegex(string pattern)
    {
        var glob = pattern.Trim().Replace('\\', '/');
        var sb = new StringBuilder("^");

        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                sb.Append(followedBySlash ? "(?:.*/)?" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
