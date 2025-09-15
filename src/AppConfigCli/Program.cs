using Azure;
using Azure.Data.AppConfiguration;
using System.Text;

namespace AppConfigCli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Prefix))
        {
            Console.Error.WriteLine("Missing required --prefix <value> argument.");
            PrintHelp();
            return 2;
        }

        var connStr = Environment.GetEnvironmentVariable("APP_CONFIG_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            Console.Error.WriteLine("APP_CONFIG_CONNECTION_STRING not set. Please export your Azure App Configuration connection string.");
            return 2;
        }

        var client = new ConfigurationClient(connStr);

        var app = new EditorApp(client, options.Prefix!, options.Label);
        try
        {
            await app.LoadAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Azure App Configuration Section Editor");
        Console.WriteLine();
        Console.WriteLine("Usage: appconfig --prefix <keyPrefix> [--label <label>]");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  APP_CONFIG_CONNECTION_STRING  Azure App Configuration connection string");
        Console.WriteLine();
        Console.WriteLine("Commands inside editor:");
        Console.WriteLine("  e <n>  Edit item n");
        Console.WriteLine("  a      Add new key under prefix");
        Console.WriteLine("  d <n>  Delete item n (confirm)");
        Console.WriteLine("  r <n>  Revert local change for n");
        Console.WriteLine("  s      Save all changes");
        Console.WriteLine("  q      Quit");
        Console.WriteLine("  h      Help");
        Console.WriteLine();
    }

    private static Options ParseArgs(string[] args)
    {
        var opts = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--prefix":
                    if (i + 1 < args.Length) { opts.Prefix = args[++i]; }
                    break;
                case "--label":
                    if (i + 1 < args.Length) { opts.Label = args[++i]; }
                    break;
                case "-h":
                case "--help":
                    opts.ShowHelp = true;
                    break;
            }
        }
        return opts;
    }

    private sealed class Options
    {
        public string? Prefix { get; set; }
        public string? Label { get; set; }
        public bool ShowHelp { get; set; }
    }
}

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

internal sealed class EditorApp
{
    private readonly ConfigurationClient _client;
    private readonly string _prefix;
    private readonly string? _label;
    private readonly List<Item> _items = new();

    public EditorApp(ConfigurationClient client, string prefix, string? label)
    {
        _client = client;
        _prefix = prefix;
        _label = label;
    }

    public async Task LoadAsync()
    {
        _items.Clear();
        var selector = new SettingSelector
        {
            KeyFilter = _prefix + "*",
            LabelFilter = _label
        };

        await foreach (var s in _client.GetConfigurationSettingsAsync(selector))
        {
            var shortKey = s.Key.StartsWith(_prefix, StringComparison.Ordinal) ? s.Key[_prefix.Length..] : s.Key;
            _items.Add(new Item
            {
                FullKey = s.Key,
                ShortKey = shortKey,
                Label = s.Label,
                OriginalValue = s.Value,
                Value = s.Value,
                State = ItemState.Unchanged
            });
        }
        _items.Sort((a, b) => string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal));
    }

    public async Task RunAsync()
    {
        while (true)
        {
            Render();
            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null) continue;
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var cmd = parts[0].ToLowerInvariant();

            switch (cmd)
            {
                case "e":
                    Edit(parts.Skip(1).ToArray());
                    break;
                case "a":
                    Add();
                    break;
                case "d":
                    Delete(parts.Skip(1).ToArray());
                    break;
                case "r":
                    Revert(parts.Skip(1).ToArray());
                    break;
                case "s":
                    await SaveAsync();
                    break;
                case "q":
                    return;
                case "h":
                case "?":
                    ShowHelp();
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'h' for help.");
                    break;
            }
        }
    }

    private void Render()
    {
        Console.Clear();
        Console.WriteLine($"Azure App Configuration Editor");
        Console.WriteLine($"Prefix: '{_prefix}'   Label filter: '{_label ?? "(any)"}'");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine("Idx  S  Key                                   Label         Value");
        Console.WriteLine(new string('-', 100));

        const int keyWidth = 35;
        const int labelWidth = 13;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var s = item.State switch
            {
                ItemState.New => '+',
                ItemState.Modified => '*',
                ItemState.Deleted => '-',
                _ => ' '
            };
            var keyDisp = TruncateFixed(item.ShortKey, keyWidth);
            var labelText = item.Label ?? "(none)";
            var labelDisp = TruncateFixed(labelText, labelWidth);
            var val = (item.Value ?? string.Empty).Replace('\n', ' ');
            if (val.Length > 40) val = val[..39] + "…";
            Console.WriteLine($"{i + 1,3}  {s}  {keyDisp,-35}  {labelDisp,-13}  {val}");
        }

        Console.WriteLine();
        Console.WriteLine("Commands: e <n>, a, d <n>, r <n>, s, q, h");
    }

    private void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Help - Commands");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine("e <n>  Edit value of item number n");
        Console.WriteLine("a      Add a new key under the current prefix");
        Console.WriteLine("d <n>  Delete item n (asks for 'yes' confirmation)");
        Console.WriteLine("r <n>  Revert local changes for item n");
        Console.WriteLine("s      Save all pending changes to Azure");
        Console.WriteLine("q      Quit the editor");
        Console.WriteLine("h/?    Show this help");
        Console.WriteLine();
        Console.WriteLine("Press Enter to return to the list...");
        Console.ReadLine();
    }

    private void Edit(string[] args)
    {
        if (!TryParseIndex(args, out var idx)) return;
        var item = _items[idx];
        var label = item.Label ?? "(none)";
        Console.WriteLine($"Editing '{item.ShortKey}' [{label}]  (Enter to save)");
        Console.Write("> ");
        var newVal = ReadLineWithInitial(item.Value ?? string.Empty);
        if (newVal is null) return;
        item.Value = newVal;
        if (!item.IsNew && item.Value != item.OriginalValue)
            item.State = ItemState.Modified;
        if (!item.IsNew && item.Value == item.OriginalValue)
            item.State = ItemState.Unchanged;
    }

    private void Add()
    {
        Console.WriteLine("Enter new key (relative to prefix):");
        Console.Write("> ");
        var k = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(k)) return;
        k = k.Trim();
        if (_items.Any(i => i.ShortKey.Equals(k, StringComparison.Ordinal)))
        {
            Console.WriteLine("Key already exists.");
            return;
        }
        Console.WriteLine("Enter value:");
        Console.Write("> ");
        var v = Console.ReadLine() ?? string.Empty;
        _items.Add(new Item
        {
            FullKey = _prefix + k,
            ShortKey = k,
            Label = _label,
            OriginalValue = null,
            Value = v,
            State = ItemState.New
        });
        _items.Sort((a, b) => string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal));
    }

    private void Delete(string[] args)
    {
        if (!TryParseIndex(args, out var idx)) return;
        var item = _items[idx];
        Console.WriteLine($"Delete '{item.ShortKey}'? Type 'yes' to confirm:");
        Console.Write("> ");
        var conf = Console.ReadLine();
        if (!string.Equals(conf, "yes", StringComparison.OrdinalIgnoreCase)) return;
        item.State = ItemState.Deleted;
    }

    private void Revert(string[] args)
    {
        if (!TryParseIndex(args, out var idx)) return;
        var item = _items[idx];
        item.Value = item.OriginalValue;
        item.State = ItemState.Unchanged;
    }

    private bool TryParseIndex(string[] args, out int idx)
    {
        idx = -1;
        if (args.Length == 0 || !int.TryParse(args[0], out var n))
        {
            Console.WriteLine("Provide an item number.");
            return false;
        }
        n -= 1;
        if (n < 0 || n >= _items.Count)
        {
            Console.WriteLine("Invalid item number.");
            return false;
        }
        idx = n;
        return true;
    }

    private async Task SaveAsync()
    {
        Console.WriteLine("Saving changes...");
        int changes = 0;

        // Upserts
        foreach (var item in _items.Where(i => i.State is ItemState.Modified or ItemState.New))
        {
            try
            {
                var setting = new ConfigurationSetting(item.FullKey, item.Value ?? string.Empty, item.Label);
                await _client.SetConfigurationSettingAsync(setting);
                item.OriginalValue = item.Value;
                item.State = ItemState.Unchanged;
                changes++;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to set '{item.ShortKey}': {ex.Message}");
            }
        }

        // Deletions
        foreach (var item in _items.Where(i => i.State == ItemState.Deleted).ToList())
        {
            try
            {
                await _client.DeleteConfigurationSettingAsync(item.FullKey, item.Label);
                _items.Remove(item);
                changes++;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to delete '{item.ShortKey}': {ex.Message}");
            }
        }

        Console.WriteLine(changes == 0 ? "No changes to save." : $"Saved {changes} change(s).");
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private static string ReadLineWithInitial(string initial)
    {
        var buffer = new StringBuilder(initial);
        int cursor = buffer.Length;
        int startLeft = Console.CursorLeft;
        int startTop = Console.CursorTop;

        void Render()
        {
            Console.SetCursorPosition(startLeft, startTop);
            var line = buffer.ToString();
            // Render line and clear any trailing characters from prior render
            Console.Write(line);
            Console.Write(" \r"); // trailing space clears if previous render was longer
            // Position cursor
            Console.SetCursorPosition(startLeft + cursor, startTop);
        }

        // Write initial
        Console.Write(buffer.ToString());

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.Remove(cursor - 1, 1);
                    cursor--;
                    Render();
                }
            }
            else if (key.Key == ConsoleKey.Delete)
            {
                if (cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                    Render();
                }
            }
            else if (key.Key == ConsoleKey.LeftArrow)
            {
                if (cursor > 0) { cursor--; Render(); }
            }
            else if (key.Key == ConsoleKey.RightArrow)
            {
                if (cursor < buffer.Length) { cursor++; Render(); }
            }
            else if (key.Key == ConsoleKey.Home)
            {
                cursor = 0; Render();
            }
            else if (key.Key == ConsoleKey.End)
            {
                cursor = buffer.Length; Render();
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Render();
            }
        }
    }

    private static string TruncateFixed(string s, int width)
    {
        if (s.Length <= width) return s;
        if (width <= 1) return new string('…', Math.Max(0, width));
        return s[..(width - 1)] + "…";
    }
}
