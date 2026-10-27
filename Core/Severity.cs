namespace LocalSonar.Core;

/// <summary>SonarQube "standard experience" severities, most severe first.</summary>
public enum Severity
{
    Blocker = 0,
    Critical = 1,
    Major = 2,
    Minor = 3,
    Info = 4,

    /// <summary>Rule not found in the rules file, so its Sonar severity is not known.</summary>
    Unknown = 5,
}

public static class SeverityParser
{
    /// <summary>
    /// Accepts standard severities (BLOCKER, CRITICAL, MAJOR, MINOR, INFO) and the newer
    /// MQR impact severities (HIGH, MEDIUM, LOW).
    /// </summary>
    public static Severity Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "BLOCKER" => Severity.Blocker,
        "CRITICAL" or "HIGH" => Severity.Critical,
        "MAJOR" or "MEDIUM" => Severity.Major,
        "MINOR" or "LOW" => Severity.Minor,
        "INFO" => Severity.Info,
        _ => Severity.Unknown,
    };
}
