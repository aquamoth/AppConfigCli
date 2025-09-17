using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AppConfigCli;

internal static class BulkEditHelper
{
    public static string BuildInitialFileContent(IEnumerable<Item> visibleItems, string? prefix, string? label)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AppConfig CLI bulk edit");
        sb.AppendLine($"# Prefix: {prefix ?? "(all)"}");
        var labelHeader = label is null ? "(any)" : (label.Length == 0 ? "(none)" : label);
        sb.AppendLine($"# Label: {labelHeader}");
        sb.AppendLine("# Format: shortKey\tvalue");
        sb.AppendLine(@"# Escape: newline as \n, tab as \t, backslash as \\");
        sb.AppendLine("# Delete a key by removing its line. Add by adding a new line.");
        foreach (var it in visibleItems.Where(i => i.State != ItemState.Deleted))
        {
            var key = it.ShortKey;
            var valEsc = EscapeValue(it.Value ?? string.Empty);
            sb.AppendLine(string.Join('\t', new[] { key, valEsc }));
        }
        return sb.ToString();
    }

    public static (int Created, int Updated, int Deleted) ApplyEdits(string fileContent, List<Item> allItems, IEnumerable<Item> visibleItemsUnderLabel, string? prefix, string? activeLabel)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith('#')) continue;
            var parts = raw.Split('\t', 2);
            if (parts.Length == 0) continue;
            string shortKey = parts[0].Trim();
            if (shortKey.Length == 0) continue;
            string valueEsc = parts.Length >= 2 ? parts[1] : string.Empty;
            var value = UnescapeValue(valueEsc);
            parsed[shortKey] = value;
        }

        var current = visibleItemsUnderLabel.Where(i => i.State != ItemState.Deleted)
                        .ToDictionary(i => i.ShortKey, i => i, StringComparer.Ordinal);

        int created = 0, updated = 0, deleted = 0;

        foreach (var kv in current)
        {
            if (!parsed.ContainsKey(kv.Key))
            {
                var item = kv.Value;
                if (item.IsNew)
                {
                    allItems.Remove(item);
                }
                else
                {
                    item.State = ItemState.Deleted;
                }
                deleted++;
            }
        }

        foreach (var kv in parsed)
        {
            var key = kv.Key;
            var newVal = kv.Value;
            if (current.TryGetValue(key, out var existing))
            {
                existing.Value = newVal;
                if (!existing.IsNew)
                {
                    existing.State = string.Equals(existing.OriginalValue ?? string.Empty, newVal, StringComparison.Ordinal)
                        ? ItemState.Unchanged
                        : ItemState.Modified;
                }
                updated++;
            }
            else
            {
                var resurrect = allItems.FirstOrDefault(i =>
                    string.Equals(i.ShortKey, key, StringComparison.Ordinal) &&
                    string.Equals(i.Label ?? string.Empty, activeLabel ?? string.Empty, StringComparison.Ordinal) &&
                    i.State == ItemState.Deleted);
                if (resurrect is not null)
                {
                    resurrect.Value = newVal;
                    resurrect.State = string.Equals(resurrect.OriginalValue ?? string.Empty, newVal, StringComparison.Ordinal)
                        ? ItemState.Unchanged
                        : ItemState.Modified;
                    updated++;
                }
                else
                {
                    var fullKey = (prefix ?? string.Empty) + key;
                    allItems.Add(new Item
                    {
                        FullKey = fullKey,
                        ShortKey = key,
                        Label = activeLabel,
                        OriginalValue = null,
                        Value = newVal,
                        State = ItemState.New
                    });
                    created++;
                }
            }
        }

        allItems.Sort((a, b) =>
        {
            int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
        });

        return (created, updated, deleted);
    }

    public static string EscapeValue(string value)
        => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

    public static string UnescapeValue(string value)
        => value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
}
