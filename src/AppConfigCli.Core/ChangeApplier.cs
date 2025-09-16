using System;
using System.Collections.Generic;
using System.Linq;

namespace AppConfigCli.Core;

public static class ChangeApplier
{
    public sealed class ChangeSet
    {
        public List<ConfigEntry> Upserts { get; } = new();
        public List<DeleteEntry> Deletes { get; } = new();
    }

    public sealed record DeleteEntry(string Key, string? Label);

    /// <summary>
    /// Computes the set of upserts and deletes to apply to the server based on local item states.
    /// - Groups by (FullKey, write-label)
    /// - If any New/Modified exists in a group, emits a single upsert using the last such item's value (last-wins)
    /// - Else, if any Deleted exists, emits a delete
    /// - Ignores Unchanged-only groups
    /// </summary>
    public static ChangeSet Compute(IEnumerable<Item> items)
    {
        var cs = new ChangeSet();

        var groups = items.GroupBy(i => (Key: i.FullKey, Label: LabelFilter.ForWrite(i.Label)));
        foreach (var g in groups)
        {
            // Prefer any New/Modified; last-wins across duplicates
            var upsertCandidate = g.LastOrDefault(i => i.State is ItemState.New or ItemState.Modified);
            if (upsertCandidate is not null)
            {
                cs.Upserts.Add(new ConfigEntry
                {
                    Key = g.Key.Key,
                    Label = g.Key.Label,
                    Value = upsertCandidate.Value ?? string.Empty
                });
                continue;
            }

            // Otherwise, if any Deleted present, emit delete
            if (g.Any(i => i.State == ItemState.Deleted))
            {
                cs.Deletes.Add(new DeleteEntry(g.Key.Key, g.Key.Label));
            }
        }

        return cs;
    }
}

