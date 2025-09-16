using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AppConfigCli.Core;

public interface IConfigRepository
{
    Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, string? labelFilter, CancellationToken ct = default);
    Task UpsertAsync(ConfigEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string key, string? label, CancellationToken ct = default);
}

