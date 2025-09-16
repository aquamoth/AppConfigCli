using System.Text.RegularExpressions;

namespace AppConfigCli.Core;

/// <summary>
/// Composes label and key regex filters over a list of items,
/// and provides helpers for mapping visible ranges back to source indices.
/// </summary>
public static class ItemFilter
{
    /// <summary>
    /// Returns the visible items that match the provided label filter and key regex.
    /// Label semantics: null = any, empty string = unlabeled only, other = literal.
    /// Preserves the original order of <paramref name="source"/>.
    /// </summary>
    public static List<Item> Visible(IEnumerable<Item> source, string? labelFilter, Regex? keyRegex)
    {
        var result = new List<Item>();
        foreach (var it in source)
        {
            if (!MatchesLabel(it.Label, labelFilter)) continue;
            if (keyRegex is not null && !keyRegex.IsMatch(it.ShortKey)) continue;
            result.Add(it);
        }
        return result;
    }

    /// <summary>
    /// Maps a 1-based [start..end] range over the visible items to indices in the source list.
    /// Returns null and sets <paramref name="error"/> when the range is out of bounds.
    /// </summary>
    public static List<int>? MapVisibleRangeToSourceIndices(IList<Item> source, string? labelFilter, Regex? keyRegex, int start, int end, out string error)
    {
        error = string.Empty;
        if (source.Count == 0) { error = "Index out of range."; return null; }

        // Compute visible count without materializing a separate list to avoid identity mapping.
        int visibleCount = 0;
        foreach (var it in source)
        {
            if (!MatchesLabel(it.Label, labelFilter)) continue;
            if (keyRegex is not null && !keyRegex.IsMatch(it.ShortKey)) continue;
            visibleCount++;
        }

        if (start > end) (start, end) = (end, start);
        if (start < 1 || end < 1 || start > visibleCount || end > visibleCount)
        {
            error = "Index out of range.";
            return null;
        }

        var indices = new List<int>();
        int visIndex = 0; // 1-based position within visible items
        for (int i = 0; i < source.Count; i++)
        {
            var it = source[i];
            if (!MatchesLabel(it.Label, labelFilter)) continue;
            if (keyRegex is not null && !keyRegex.IsMatch(it.ShortKey)) continue;
            visIndex++;
            if (visIndex >= start && visIndex <= end)
            {
                indices.Add(i);
            }
            if (visIndex > end) break;
        }
        return indices;
    }

    /// <summary>
    /// Returns the indices (into <paramref name="source"/>) of items visible under the given filters.
    /// Useful when the caller needs to preserve object identity in a parallel UI list.
    /// </summary>
    public static List<int> VisibleIndices(IList<Item> source, string? labelFilter, Regex? keyRegex)
    {
        var result = new List<int>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var it = source[i];
            if (!MatchesLabel(it.Label, labelFilter)) continue;
            if (keyRegex is not null && !keyRegex.IsMatch(it.ShortKey)) continue;
            result.Add(i);
        }
        return result;
    }

    private static bool MatchesLabel(string? itemLabel, string? filterLabel)
    {
        if (filterLabel is null) return true; // any
        if (filterLabel.Length == 0) return string.IsNullOrEmpty(itemLabel); // unlabeled only
        return string.Equals(itemLabel, filterLabel, StringComparison.Ordinal);
    }
}
