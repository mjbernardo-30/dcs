using System.Runtime.InteropServices;

namespace LocalSonar.Analysis.Duplication;

/// <summary>
/// Copy-paste detection in the style of SonarQube CPD for C#:
/// a duplicate is a run of at least <c>minTokens</c> identical (normalized) tokens, found anywhere
/// in the scanned files (same file or another file), spread over at least <c>minLines</c> lines.
///
/// Approach: rolling hash over every window of minTokens tokens; windows seen more than once are
/// verified token-by-token and their tokens marked duplicated; contiguous marked tokens form
/// blocks; blocks spanning enough lines count their lines as duplicated.
/// </summary>
public static class DuplicationDetector
{
    private const ulong HashBase = 1_000_003UL;

    public static DuplicationReport Detect(IReadOnlyList<TokenizedFile> files, int minTokens, int minLines)
    {
        var marked = files.Select(f => new bool[f.TokenIds.Length]).ToArray();
        var partner = files.Select(f => new (int File, int Pos)?[f.TokenIds.Length]).ToArray();

        // Pass 1: count window hashes. Pass 2: keep positions only for hashes seen twice or more.
        var counts = new Dictionary<ulong, int>();
        ForEachWindow(files, minTokens, (hash, _, _) =>
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, hash, out _);
            count++;
        });

        var occurrences = new Dictionary<ulong, List<(int File, int Pos)>>();
        ForEachWindow(files, minTokens, (hash, fileIndex, pos) =>
        {
            if (counts[hash] < 2) return;
            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(occurrences, hash, out _);
            (list ??= []).Add((fileIndex, pos));
        });
        counts.Clear();

        foreach (var group in occurrences.Values)
        {
            // Split by actual token equality (guards against hash collisions).
            var buckets = new List<List<(int File, int Pos)>>();
            foreach (var occ in group)
            {
                var window = files[occ.File].TokenIds.AsSpan(occ.Pos, minTokens);
                List<(int File, int Pos)>? bucket = null;
                foreach (var candidate in buckets)
                {
                    var first = candidate[0];
                    if (files[first.File].TokenIds.AsSpan(first.Pos, minTokens).SequenceEqual(window))
                    {
                        bucket = candidate;
                        break;
                    }
                }

                if (bucket is null) buckets.Add([occ]); else bucket.Add(occ);
            }

            foreach (var bucket in buckets.Where(b => b.Count > 1))
            {
                for (var i = 0; i < bucket.Count; i++)
                {
                    var (f, p) = bucket[i];
                    Array.Fill(marked[f], true, p, minTokens);
                    partner[f][p] ??= bucket[(i + 1) % bucket.Count];
                }
            }
        }

        occurrences.Clear();

        var fileResults = new List<DuplicationFileResult>(files.Count);
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            fileResults.Add(BuildFileResult(files, fileIndex, marked[fileIndex], partner[fileIndex], minLines));
        }

        return new DuplicationReport(fileResults, minTokens, minLines);
    }

    private static DuplicationFileResult BuildFileResult(
        IReadOnlyList<TokenizedFile> files,
        int fileIndex,
        bool[] marked,
        (int File, int Pos)?[] partner,
        int minLines)
    {
        var file = files[fileIndex];
        var duplicatedLines = new HashSet<int>();
        var blocks = new List<DuplicatedBlock>();

        var i = 0;
        while (i < marked.Length)
        {
            if (!marked[i]) { i++; continue; }

            var start = i;
            while (i < marked.Length && marked[i]) i++;
            var end = i - 1;

            var startLine = file.StartLines[start];
            var endLine = file.EndLines[end];
            if (endLine - startLine + 1 < minLines)
            {
                continue;
            }

            for (var line = startLine; line <= endLine; line++)
            {
                duplicatedLines.Add(line);
            }

            string? otherFile = null;
            var otherLine = 0;
            for (var k = start; k <= end; k++)
            {
                if (partner[k] is { } other)
                {
                    otherFile = files[other.File].RelativePath;
                    otherLine = files[other.File].StartLines[other.Pos];
                    break;
                }
            }

            blocks.Add(new DuplicatedBlock(startLine, endLine, otherFile, otherLine));
        }

        return new DuplicationFileResult(file.FilePath, file.RelativePath, file.TotalLines, duplicatedLines.Count, blocks);
    }

    private static void ForEachWindow(IReadOnlyList<TokenizedFile> files, int n, Action<ulong, int, int> onWindow)
    {
        // HashBase^(n-1), wrapping arithmetic (mod 2^64) is intended.
        ulong highPower = 1;
        for (var i = 0; i < n - 1; i++) highPower = unchecked(highPower * HashBase);

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var ids = files[fileIndex].TokenIds;
            if (ids.Length < n) continue;

            ulong hash = 0;
            for (var i = 0; i < n; i++)
            {
                hash = unchecked(hash * HashBase + (ulong)ids[i]);
            }

            onWindow(hash, fileIndex, 0);

            for (var pos = 1; pos + n <= ids.Length; pos++)
            {
                hash = unchecked((hash - (ulong)ids[pos - 1] * highPower) * HashBase + (ulong)ids[pos + n - 1]);
                onWindow(hash, fileIndex, pos);
            }
        }
    }
}
