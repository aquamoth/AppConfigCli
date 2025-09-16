using System.Collections.Generic;
using System;
using System.Linq;

namespace AppConfigCli.Core;

public sealed class AppStateReconciler
{
    public IReadOnlyList<Item> Reconcile(
        string prefix,
        string? activeLabel,
        IEnumerable<Item> local,
        IEnumerable<ConfigEntry> server)
    {
        var locals = local.ToDictionary<Item, (string, string), Item>(
            i => (i.FullKey, (i.Label ?? string.Empty)), i => i,
            EqualityComparer<(string, string)>.Default);

        var fresh = new List<Item>();
        var seen = new HashSet<(string Key, string Label)>();

        foreach (var s in server)
        {
            var lbl = s.Label ?? string.Empty;
            seen.Add((s.Key, lbl));

            var shortKey = s.Key.StartsWith(prefix, StringComparison.Ordinal)
                ? s.Key[prefix.Length..]
                : s.Key;

            if (!locals.TryGetValue((s.Key, lbl), out var l))
            {
                fresh.Add(new Item { FullKey = s.Key, ShortKey = shortKey, Label = s.Label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Unchanged });
                continue;
            }

            switch (l.State)
            {
                case ItemState.Deleted:
                    fresh.Add(new Item { FullKey = s.Key, ShortKey = shortKey, Label = s.Label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Deleted });
                    break;
                case ItemState.New:
                {
                    var state = string.Equals(l.Value ?? string.Empty, s.Value ?? string.Empty, StringComparison.Ordinal)
                        ? ItemState.Unchanged
                        : ItemState.Modified;
                    fresh.Add(new Item { FullKey = s.Key, ShortKey = shortKey, Label = s.Label, OriginalValue = s.Value, Value = l.Value, State = state });
                    break;
                }
                case ItemState.Modified:
                {
                    var eq = string.Equals(l.Value ?? string.Empty, s.Value ?? string.Empty, StringComparison.Ordinal);
                    fresh.Add(new Item { FullKey = s.Key, ShortKey = shortKey, Label = s.Label, OriginalValue = s.Value, Value = eq ? s.Value : l.Value, State = eq ? ItemState.Unchanged : ItemState.Modified });
                    break;
                }
                default:
                    fresh.Add(new Item { FullKey = s.Key, ShortKey = shortKey, Label = s.Label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Unchanged });
                    break;
            }
        }

        foreach (var kv in locals.Values)
        {
            var lbl = kv.Label ?? string.Empty;
            if (seen.Contains((kv.FullKey, lbl))) continue;

            var shortKey = kv.FullKey.StartsWith(prefix, StringComparison.Ordinal)
                ? kv.FullKey[prefix.Length..]
                : kv.FullKey;

            if (kv.State is ItemState.New or ItemState.Modified)
            {
                fresh.Add(new Item { FullKey = kv.FullKey, ShortKey = shortKey, Label = kv.Label, OriginalValue = null, Value = kv.Value, State = ItemState.New });
            }
        }

        fresh.Sort((a, b) =>
        {
            int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
        });

        return fresh;
    }
}
