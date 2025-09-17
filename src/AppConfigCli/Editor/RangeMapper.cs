using System.Collections.Generic;
using System.Text.RegularExpressions;
using AppConfigCli.Core;

namespace AppConfigCli;

internal static class RangeMapper
{
    /// <summary>
    /// Maps 1-based [start..end] over the visible items (computed from label + regex)
    /// to indices in the original list. Returns null and error when out of bounds.
    /// </summary>
    public static List<int>? Map(IList<Item> source, string? labelFilter, Regex? keyRegex, int start, int end, out string error)
    {
        // Convert UI items to Core items for consistent semantics
        var mapper = new EditorMappers();
        var coreList = new List<Core.Item>(source.Count);
        foreach (var it in source) coreList.Add(mapper.ToCoreItem(it));
        var indices = ItemFilter.MapVisibleRangeToSourceIndices(coreList, labelFilter, keyRegex, start, end, out error);
        return indices;
    }
}

