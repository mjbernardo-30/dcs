namespace LocalSonar.Analysis.Duplication;

public sealed record DuplicatedBlock(int StartLine, int EndLine, string? DuplicateOfFile, int DuplicateOfLine)
{
    public int LineCount => EndLine - StartLine + 1;
}

public sealed record DuplicationFileResult(
    string FilePath,
    string RelativePath,
    int TotalLines,
    int DuplicatedLines,
    IReadOnlyList<DuplicatedBlock> Blocks)
{
    public double DuplicatedLinesPercent => TotalLines == 0 ? 0 : 100.0 * DuplicatedLines / TotalLines;
}

public sealed record DuplicationReport(IReadOnlyList<DuplicationFileResult> Files, int MinimumTokens, int MinimumLines)
{
    public int TotalLines => Files.Sum(f => f.TotalLines);
    public int DuplicatedLines => Files.Sum(f => f.DuplicatedLines);
    public int DuplicatedBlocks => Files.Sum(f => f.Blocks.Count);

    /// <summary>Same formula as Sonar's "Duplicated Lines (%)": duplicated_lines / lines * 100.</summary>
    public double DuplicatedLinesPercent => TotalLines == 0 ? 0 : 100.0 * DuplicatedLines / TotalLines;
}
