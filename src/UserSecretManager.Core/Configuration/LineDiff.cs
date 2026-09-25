namespace UserSecretManager.Core.Configuration;

/// <summary>Kind of a line in a diff.</summary>
public enum DiffLineKind
{
    /// <summary>Line is present in both versions.</summary>
    Unchanged,

    /// <summary>Line exists only in the old version.</summary>
    Removed,

    /// <summary>Line exists only in the new version.</summary>
    Added,

    /// <summary>Marker for skipped unchanged lines.</summary>
    Gap,
}

/// <summary>One line of a diff.</summary>
/// <param name="Kind">Line kind.</param>
/// <param name="OldNumber">1-based line number in the old text, if any.</param>
/// <param name="NewNumber">1-based line number in the new text, if any.</param>
/// <param name="Text">Line text.</param>
public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);

/// <summary>Minimal line-based diff for previewing configuration edits.</summary>
public static class LineDiff
{
    private const int MaxCells = 4_000_000;

    /// <summary>
    /// Computes the diff between two texts and keeps <paramref name="context"/> unchanged lines around each change.
    /// </summary>
    public static IReadOnlyList<DiffLine> Compute(string oldText, string newText, int context = 3)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        var full = Diff(SplitLines(oldText), SplitLines(newText));
        return Collapse(full, context);
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static List<DiffLine> Diff(string[] oldLines, string[] newLines)
    {
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length &&
               string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               string.Equals(oldLines[^(suffix + 1)], newLines[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        var result = new List<DiffLine>();
        for (var i = 0; i < prefix; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, i + 1, i + 1, oldLines[i]));
        }

        result.AddRange(DiffMiddle(oldLines[prefix..^suffix], newLines[prefix..^suffix], prefix));

        for (var i = 0; i < suffix; i++)
        {
            var oldIndex = oldLines.Length - suffix + i;
            var newIndex = newLines.Length - suffix + i;
            result.Add(new DiffLine(DiffLineKind.Unchanged, oldIndex + 1, newIndex + 1, oldLines[oldIndex]));
        }

        return result;
    }

    private static List<DiffLine> DiffMiddle(string[] oldLines, string[] newLines, int offset)
    {
        var result = new List<DiffLine>();
        if ((long)oldLines.Length * newLines.Length > MaxCells)
        {
            result.AddRange(oldLines.Select((l, i) => new DiffLine(DiffLineKind.Removed, offset + i + 1, null, l)));
            result.AddRange(newLines.Select((l, i) => new DiffLine(DiffLineKind.Added, null, offset + i + 1, l)));
            return result;
        }

        var lcs = BuildLcsTable(oldLines, newLines);
        int x = 0, y = 0;
        while (x < oldLines.Length || y < newLines.Length)
        {
            if (x < oldLines.Length && y < newLines.Length &&
                string.Equals(oldLines[x], newLines[y], StringComparison.Ordinal))
            {
                result.Add(new DiffLine(DiffLineKind.Unchanged, offset + x + 1, offset + y + 1, oldLines[x]));
                x++;
                y++;
            }
            else if (x < oldLines.Length && (y == newLines.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                result.Add(new DiffLine(DiffLineKind.Removed, offset + x + 1, null, oldLines[x]));
                x++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Added, null, offset + y + 1, newLines[y]));
                y++;
            }
        }

        return result;
    }

    private static int[,] BuildLcsTable(string[] oldLines, string[] newLines)
    {
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        return lcs;
    }

    private static List<DiffLine> Collapse(List<DiffLine> lines, int context)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffLineKind.Unchanged)
            {
                continue;
            }

            for (var k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++)
            {
                keep[k] = true;
            }
        }

        var result = new List<DiffLine>();
        var skipping = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                result.Add(lines[i]);
                skipping = false;
            }
            else if (!skipping)
            {
                result.Add(new DiffLine(DiffLineKind.Gap, null, null, "…"));
                skipping = true;
            }
        }

        return result;
    }
}
