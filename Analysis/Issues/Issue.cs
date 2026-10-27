using LocalSonar.Core;

namespace LocalSonar.Analysis.Issues;

public sealed record Issue(
    string RuleId,
    Severity Severity,
    string Message,
    string FilePath,
    string RelativePath,
    int Line,
    int Column,
    string? ProjectName,
    string? RuleName,
    string? HelpUri);
