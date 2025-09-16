namespace AppConfigCli.Core;

public enum ItemState { Unchanged, Modified, New, Deleted }

public sealed class Item
{
    public required string FullKey { get; init; }
    public required string ShortKey { get; init; }
    public string? Label { get; init; }
    public string? OriginalValue { get; set; }
    public string? Value { get; set; }
    public ItemState State { get; set; } = ItemState.Unchanged;
}

public sealed class ConfigEntry
{
    public required string Key { get; init; }
    public string? Label { get; init; }
    public required string Value { get; init; }
}
