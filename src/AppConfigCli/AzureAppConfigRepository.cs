using Azure;
using Azure.Data.AppConfiguration;
using AppConfigCli.Core;

namespace AppConfigCli;

internal sealed class AzureAppConfigRepository : IConfigRepository
{
    private readonly ConfigurationClient _client;

    public AzureAppConfigRepository(ConfigurationClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, string? labelFilter, CancellationToken ct = default)
    {
        var selector = new SettingSelector
        {
            KeyFilter = string.IsNullOrWhiteSpace(prefix) ? "*" : prefix + "*",
            LabelFilter = Core.LabelFilter.ForSelector(labelFilter)
        };

        var list = new List<ConfigEntry>();
        await foreach (var s in _client.GetConfigurationSettingsAsync(selector, ct))
        {
            list.Add(new ConfigEntry { Key = s.Key, Label = s.Label, Value = s.Value ?? string.Empty });
        }
        return list;
    }

    public async Task UpsertAsync(ConfigEntry entry, CancellationToken ct = default)
    {
        var writeLabel = Core.LabelFilter.ForWrite(entry.Label);
        var setting = new ConfigurationSetting(entry.Key, entry.Value ?? string.Empty, writeLabel);
        await _client.SetConfigurationSettingAsync(setting, cancellationToken: ct);
    }

    public Task DeleteAsync(string key, string? label, CancellationToken ct = default)
        => _client.DeleteConfigurationSettingAsync(key, Core.LabelFilter.ForWrite(label), ct);
}

