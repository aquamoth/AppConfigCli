namespace AppConfigCli;

internal enum ItemState { Unchanged, Modified, New, Deleted }

internal sealed class Item
{
    public required string FullKey { get; init; }
    public required string ShortKey { get; init; }
    public string? Label { get; init; }
    public string? OriginalValue { get; set; }
    public string? Value { get; set; }
    public ItemState State { get; set; } = ItemState.Unchanged;
    public bool IsNew => State == ItemState.New;
    public bool IsDeleted => State == ItemState.Deleted;
}

