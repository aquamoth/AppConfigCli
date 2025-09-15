using Azure;
using Azure.Data.AppConfiguration;
using System.Text;
using Azure.Identity;
using Azure.Core;

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
        ConfigurationClient client;
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            client = new ConfigurationClient(connStr);
        }
        else
        {
            // Fallback to AAD auth via endpoint + interactive/device/browser auth
            var endpoint = Environment.GetEnvironmentVariable("APP_CONFIG_ENDPOINT");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Console.WriteLine("APP_CONFIG_CONNECTION_STRING not set.");
                Console.WriteLine("Enter Azure App Configuration endpoint (e.g., https://<name>.azconfig.io):");
                Console.Write("> ");
                endpoint = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    Console.Error.WriteLine("No endpoint provided. Exiting.");
                    return 2;
                }
            }

            var uri = new Uri(endpoint!);
            var credential = BuildInteractiveCredential();
            client = new ConfigurationClient(uri, credential);
        }

        var app = new EditorApp(client, options.Prefix!, options.Label);
        try
        {
            await app.LoadAsync();
            await app.RunAsync();
            return 0;
        }
        catch (RequestFailedException rfe) when (rfe.Status == 403)
        {
            Console.Error.WriteLine("Forbidden (403): The signed-in identity lacks App Configuration data access.");
            Console.Error.WriteLine("Grant App Configuration Data Reader (read) or Data Owner (read/write) on the target App Configuration resource.");
            Console.Error.WriteLine("Alternatively, use a connection string in APP_CONFIG_CONNECTION_STRING.");
            Console.Error.WriteLine("Tip: If you were silently authenticated via Azure CLI, try signing in interactively with a different account by logging out of Azure CLI or using the device code prompt.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static TokenCredential BuildInteractiveCredential()
    {
        var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var browser = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            DisableAutomaticAuthentication = false
        });
        var cli = new AzureCliCredential();
        var vsc = new VisualStudioCodeCredential(new VisualStudioCodeCredentialOptions
        {
            TenantId = tenantId
        });

        var device = new DeviceCodeCredential(new DeviceCodeCredentialOptions
        {
            TenantId = tenantId,
            ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46",
            DeviceCodeCallback = (code, ct) =>
            {
                Console.WriteLine();
                Console.WriteLine("To sign in, open the browser to:");
                Console.WriteLine(code.VerificationUri);
                Console.WriteLine("and enter the code:");
                Console.WriteLine(code.UserCode);
                Console.WriteLine();
                return Task.CompletedTask;
            }
        });

        // Prefer interactive and device code to give the user a chance to choose the right identity.
        // CLI/VS Code fallbacks are last.
        return new ChainedTokenCredential(browser, device, cli, vsc);
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
        Console.WriteLine("  a|add           Add new key under prefix");
        Console.WriteLine("  d|delete <n>    Delete item n");
        Console.WriteLine("  e|edit <n>      Edit item n");
        Console.WriteLine("  h|help          Help");
        Console.WriteLine("  l|label [value] Change label filter (no arg clears)");
        Console.WriteLine("  q|quit          Quit");
        Console.WriteLine("  r|reload        Reload settings from Azure");
        Console.WriteLine("  s|save          Save all changes");
        Console.WriteLine("  u|undo <n>      Undo local change for n");
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
    private string? _label;
    private readonly List<Item> _items = new();

    public EditorApp(ConfigurationClient client, string prefix, string? label)
    {
        _client = client;
        _prefix = prefix;
        _label = label;
    }

    public async Task LoadAsync()
    {
        // Capture local snapshot to reapply after fetch
        var local = _items.ToDictionary(i => MakeKey(i.FullKey, i.Label), i => new
        {
            i.State,
            i.Value,
            i.OriginalValue
        });

        var fresh = new List<Item>();

        var selector = new SettingSelector
        {
            KeyFilter = _prefix + "*",
            LabelFilter = _label
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var s in _client.GetConfigurationSettingsAsync(selector))
        {
            var fullKey = s.Key;
            var label = s.Label;
            var shortKey = fullKey.StartsWith(_prefix, StringComparison.Ordinal) ? fullKey[_prefix.Length..] : fullKey;
            var key = MakeKey(fullKey, label);
            seen.Add(key);

            if (local.TryGetValue(key, out var l))
            {
                if (l.State == ItemState.Deleted)
                {
                    fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Deleted });
                }
                else if (l.State == ItemState.New)
                {
                    var localVal = l.Value ?? string.Empty;
                    var state = string.Equals(localVal, s.Value, StringComparison.Ordinal) ? ItemState.Unchanged : ItemState.Modified;
                    fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = localVal, State = state });
                }
                else if (l.State == ItemState.Modified)
                {
                    var localVal = l.Value ?? string.Empty;
                    if (string.Equals(localVal, s.Value, StringComparison.Ordinal))
                        fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Unchanged });
                    else
                        fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = localVal, State = ItemState.Modified });
                }
                else
                {
                    fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Unchanged });
                }
            }
            else
            {
                fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = s.Value, Value = s.Value, State = ItemState.Unchanged });
            }
        }

        // Add locals missing on server
        foreach (var kv in local)
        {
            if (seen.Contains(kv.Key)) continue;
            SplitKey(kv.Key, out var fullKey, out var label);
            var shortKey = fullKey.StartsWith(_prefix, StringComparison.Ordinal) ? fullKey[_prefix.Length..] : fullKey;
            switch (kv.Value.State)
            {
                case ItemState.New:
                    fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = null, Value = kv.Value.Value, State = ItemState.New });
                    break;
                case ItemState.Modified:
                    // Edited locally but deleted on server => treat as new
                    fresh.Add(new Item { FullKey = fullKey, ShortKey = shortKey, Label = label, OriginalValue = null, Value = kv.Value.Value, State = ItemState.New });
                    break;
                case ItemState.Deleted:
                    // Already gone on server, drop
                    break;
                default:
                    // Unchanged but deleted server-side, drop
                    break;
            }
        }

        fresh.Sort(CompareItems);
        _items.Clear();
        _items.AddRange(fresh);
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
                case "edit":
                    Edit(parts.Skip(1).ToArray());
                    break;
                case "a":
                case "add":
                    Add();
                    break;
                case "d":
                case "delete":
                    Delete(parts.Skip(1).ToArray());
                    break;
                case "u":
                case "undo":
                    Undo(parts.Skip(1).ToArray());
                    break;
                case "label":
                case "l":
                    await ChangeLabelAsync(parts.Skip(1).ToArray());
                    break;
                case "s":
                case "save":
                    await SaveAsync();
                    break;
                case "r":
                case "reload":
                    await LoadAsync();
                    break;
                case "q":
                case "quit":
                case "exit":
                    return;
                case "h":
                case "?":
                case "help":
                    ShowHelp();
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'h' for help.");
                    break;
            }
        }
    }

    private static string MakeKey(string fullKey, string? label)
        => fullKey + "\n" + (label ?? string.Empty);

    private static void SplitKey(string composite, out string fullKey, out string? label)
    {
        var idx = composite.IndexOf('\n');
        if (idx < 0) { fullKey = composite; label = null; return; }
        fullKey = composite[..idx];
        var rest = composite[(idx + 1)..];
        label = rest.Length == 0 ? null : rest;
    }

    private void Render()
    {
        Console.Clear();
        Console.WriteLine($"Azure App Configuration Editor");
        Console.WriteLine($"Prefix: '{_prefix}'   Label filter: '{_label ?? "(any)"}'");

        var width = GetWindowWidth();
        bool includeValue = width >= 60; // minimal width for value column
        ComputeLayout(width, includeValue, out var keyWidth, out var labelWidth, out var valueWidth);

        Console.WriteLine(new string('-', width));
        if (includeValue)
            Console.WriteLine($"Idx  S  {PadColumn("Key", keyWidth)}  {PadColumn("Label", labelWidth)}  Value");
        else
            Console.WriteLine($"Idx  S  {PadColumn("Key", keyWidth)}  {PadColumn("Label", labelWidth)}");
        Console.WriteLine(new string('-', width));

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
            if (valueWidth > 0)
            {
                var valFull = (item.Value ?? string.Empty).Replace('\n', ' ');
                var val = TruncateFixed(valFull, valueWidth);
                Console.WriteLine($"{i + 1,3}  {s}  {PadColumn(keyDisp, keyWidth)}  {PadColumn(labelDisp, labelWidth)}  {val}");
            }
            else
            {
                Console.WriteLine($"{i + 1,3}  {s}  {PadColumn(keyDisp, keyWidth)}  {PadColumn(labelDisp, labelWidth)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Commands: a|add, d|delete <n>, e|edit <n>, h|help, l|label [value], q|quit, r|reload, s|save, u|undo <n>");
    }

    private void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Help - Commands");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine("a|add            Add a new key under the current prefix");
        Console.WriteLine("d|delete <n>     Delete item n");
        Console.WriteLine("e|edit <n>       Edit value of item number n");
        Console.WriteLine("h|help|?         Show this help");
        Console.WriteLine("l|label [value]  Change label filter (no arg clears)");
        Console.WriteLine("q|quit           Quit the editor");
        Console.WriteLine("r|reload         Reload settings from Azure (discards local edits)");
        Console.WriteLine("s|save           Save all pending changes to Azure");
        Console.WriteLine("u|undo <n>       Undo local changes for item n");
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
        
        string? chosenLabel = _label;
        if (chosenLabel is not null)
        {
            Console.WriteLine($"Adding new key under label: [{chosenLabel}]");
        }
        else
        {
            Console.WriteLine("Enter label for new key (empty for none):");
            Console.Write("> ");
            var lbl = Console.ReadLine();
            chosenLabel = string.IsNullOrWhiteSpace(lbl) ? null : lbl!.Trim();
            Console.WriteLine($"Using label: [{chosenLabel ?? "(none)"}]");
        }

        // Prevent duplicates only for the same label
        if (_items.Any(i => i.ShortKey.Equals(k, StringComparison.Ordinal)
                            && string.Equals(i.Label ?? string.Empty, chosenLabel ?? string.Empty, StringComparison.Ordinal)))
        {
            Console.WriteLine("Key already exists for this label.");
            return;
        }

        Console.WriteLine("Enter value:");
        Console.Write("> ");
        var v = Console.ReadLine() ?? string.Empty;
        _items.Add(new Item
        {
            FullKey = _prefix + k,
            ShortKey = k,
            Label = chosenLabel,
            OriginalValue = null,
            Value = v,
            State = ItemState.New
        });
        _items.Sort(CompareItems);
    }

    private void Delete(string[] args)
    {
        if (!TryParseIndex(args, out var idx)) return;
        var item = _items[idx];
        if (item.IsNew)
        {
            _items.RemoveAt(idx);
        }
        else
        {
            item.State = ItemState.Deleted;
        }
    }

    private void Undo(string[] args)
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

    private async Task ChangeLabelAsync(string[] args)
    {
        if (args.Length == 0)
        {
            // No argument clears the filter
            _label = null;
            await LoadAsync();
            return;
        }

        var newLabelArg = string.Join(' ', args).Trim();
        _label = newLabelArg; // accept any value literally, including "clear"
        await LoadAsync();
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
        int cursor = buffer.Length; // insertion index in buffer
        int startLeft = Console.CursorLeft;
        int startTop = Console.CursorTop;
        int scrollStart = 0; // index in buffer where the viewport starts

        // If there is effectively no room on this line, move to a fresh line
        int initialAvail = Math.Max(0, Console.WindowWidth - startLeft - 1);
        if (initialAvail < 10)
        {
            Console.WriteLine();
            startLeft = 0;
            startTop = Console.CursorTop;
        }

        void Render()
        {
            int winWidth;
            try { winWidth = Console.WindowWidth; }
            catch { winWidth = 80; }

            int contentWidth = Math.Max(1, winWidth - startLeft - 1);

            // Keep cursor within viewport
            if (cursor < scrollStart) scrollStart = cursor;
            if (cursor - scrollStart >= contentWidth) scrollStart = Math.Max(0, cursor - contentWidth + 1);

            int end = Math.Min(buffer.Length, scrollStart + contentWidth);
            string view = buffer.ToString(scrollStart, end - scrollStart);

            // Show ellipsis if scrolled left/right
            if (scrollStart > 0 && view.Length > 0)
            {
                view = '…' + (view.Length > 1 ? view[1..] : string.Empty);
            }
            if (end < buffer.Length && view.Length > 0)
            {
                view = (view.Length > 1 ? view[..^1] : string.Empty) + '…';
            }

            // Render view padded to the full content width to clear remnants
            Console.SetCursorPosition(startLeft, startTop);
            Console.Write(view.PadRight(contentWidth));

            // Place cursor within the view
            int cursorCol = startLeft + Math.Min(cursor - scrollStart, contentWidth - 1);
            int safeCol = Math.Min(Math.Max(0, winWidth - 1), Math.Max(0, cursorCol));
            try { Console.SetCursorPosition(safeCol, startTop); } catch { }
        }

        // Initial render
        Render();

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

    private static string PadColumn(string text, int width)
    {
        var t = TruncateFixed(text, width);
        if (t.Length < width) return t.PadRight(width);
        return t;
    }

    

    private static int CompareItems(Item a, Item b)
    {
        int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
        if (c != 0) return c;
        return string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
    }

    private static int GetWindowWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            // Allow narrow widths; we handle hiding columns below thresholds
            return Math.Max(20, Math.Min(w, 240));
        }
        catch
        {
            return 100;
        }
    }

    private void ComputeLayout(int totalWidth, bool includeValue, out int keyWidth, out int labelWidth, out int valueWidth)
    {
        const int minKey = 15;
        const int maxKey = 80;
        const int minLabel = 8;
        const int maxLabel = 25;
        const int minValue = 10;

        // Determine label width from data (clamped)
        var labelMax = Math.Max(6, _items.Select(i => (i.Label ?? "(none)").Length).DefaultIfEmpty(6).Max());
        labelWidth = Math.Clamp(labelMax, minLabel, maxLabel);

        // Fixed non-column characters in a row
        int fixedChars = includeValue ? 12 : 10;

        // Available space for key + label (+ value)
        int available = totalWidth - (fixedChars + labelWidth);

        int longestKey = _items.Select(i => i.ShortKey.Length).DefaultIfEmpty(minKey).Max();

        if (includeValue)
        {
            if (available < minKey + minValue)
            {
                // Squeeze label when very narrow
                int deficit = (minKey + minValue) - available;
                labelWidth = Math.Max(minLabel, labelWidth - deficit);
                available = totalWidth - (fixedChars + labelWidth);
            }

            int neededKey = Math.Clamp(longestKey, minKey, Math.Min(maxKey, available - minValue));
            keyWidth = neededKey;
            valueWidth = available - keyWidth;

            if (valueWidth < minValue)
            {
                int shortage = minValue - valueWidth;
                keyWidth = Math.Max(minKey, keyWidth - shortage);
                valueWidth = available - keyWidth;
            }

            keyWidth = Math.Max(minKey, keyWidth);
            valueWidth = Math.Max(1, valueWidth);
        }
        else
        {
            if (available < minKey)
            {
                int deficit = minKey - available;
                labelWidth = Math.Max(minLabel, labelWidth - deficit);
                available = totalWidth - (fixedChars + labelWidth);
            }

            keyWidth = Math.Clamp(longestKey, minKey, Math.Min(maxKey, available));
            valueWidth = 0;
        }
    }
}
