using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AppConfigCli.Core;

public sealed class InMemoryConfigRepository : IConfigRepository
{
    // Keyed by (Key, WriteLabel)
    private readonly ConcurrentDictionary<(string Key, string? Label), string> _store = new();

    public InMemoryConfigRepository()
    {
    }

    public InMemoryConfigRepository(IEnumerable<ConfigEntry> seed)
    {
        foreach (var e in seed)
        {
            _store[(e.Key, LabelFilter.ForWrite(e.Label))] = e.Value;
        }
    }

    public Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, string? labelFilter, CancellationToken ct = default)
    {
        var list = new List<ConfigEntry>();
        foreach (var kv in _store)
        {
            if (!string.IsNullOrEmpty(prefix) && !kv.Key.Key.StartsWith(prefix!, System.StringComparison.Ordinal))
                continue;

            // Selector label semantics: null=any, ""=unlabeled only, else literal
            if (labelFilter is null)
            {
                // any
            }
            else if (labelFilter.Length == 0)
            {
                if (kv.Key.Label is not null) continue;
            }
            else
            {
                if (!string.Equals(kv.Key.Label, labelFilter, System.StringComparison.Ordinal)) continue;
            }

            list.Add(new ConfigEntry { Key = kv.Key.Key, Label = kv.Key.Label, Value = kv.Value });
        }

        // Stable sort for deterministic tests
        list.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyList<ConfigEntry>>(list);
    }

    public Task UpsertAsync(ConfigEntry entry, CancellationToken ct = default)
    {
        _store[(entry.Key, LabelFilter.ForWrite(entry.Label))] = entry.Value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, string? label, CancellationToken ct = default)
    {
        _store.TryRemove((key, LabelFilter.ForWrite(label)), out _);
        return Task.CompletedTask;
    }
}

