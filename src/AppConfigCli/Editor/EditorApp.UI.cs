using System;
using System.Collections.Generic;
using System.Linq;
using AppConfigCli.Core.UI;

namespace AppConfigCli;

internal sealed partial class EditorApp
{
    private void Render()
    {
        Console.Clear();
        Console.WriteLine("Azure App Configuration Editor");
        var prefixDisplay = string.IsNullOrWhiteSpace(_prefix) ? "(none)" : _prefix;
        var labelDisplay = _label is null ? "(any)" : (_label.Length == 0 ? "(none)" : _label);
        var keyRegexDisplay = string.IsNullOrEmpty(_keyRegexPattern) ? "(none)" : _keyRegexPattern;
        Console.WriteLine($"Prefix: '{prefixDisplay}'   Label filter: '{labelDisplay}'   Key regex: '{keyRegexDisplay}'   Auth: {_authModeDesc}");

        var width = GetWindowWidth();
        bool includeValue = width >= 60; // minimal width for value column
        var visible = GetVisibleItems();

        // Layout calculation via Core UI helper (map UI -> Core items)
        var layoutMapper = new EditorMappers();
        var coreVisible = visible.Select(layoutMapper.ToCoreItem).ToList();
        TableLayout.Compute(width, includeValue, coreVisible, out var keyWidth, out var labelWidth, out var valueWidth);

        Console.WriteLine(new string('-', width));
        if (includeValue)
            Console.WriteLine($"Idx  S  {PadColumn("Key", keyWidth)}  {PadColumn("Label", labelWidth)}  Value");
        else
            Console.WriteLine($"Idx  S  {PadColumn("Key", keyWidth)}  {PadColumn("Label", labelWidth)}");
        Console.WriteLine(new string('-', width));

        for (int i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            var s = item.State switch
            {
                ItemState.New => '+',
                ItemState.Modified => '*',
                ItemState.Deleted => '-',
                _ => ' '
            };
            var keyDisp = TextTruncation.TruncateFixed(item.ShortKey, keyWidth);
            var labelText = string.IsNullOrEmpty(item.Label) ? "(none)" : item.Label;
            var labelDisp = TextTruncation.TruncateFixed(labelText, labelWidth);
            if (valueWidth > 0)
            {
                var valFull = (item.Value ?? string.Empty).Replace('\n', ' ');
                var val = TextTruncation.TruncateFixed(valFull, valueWidth);
                Console.WriteLine($"{i + 1,3}  {s}  {PadColumn(keyDisp, keyWidth)}  {PadColumn(labelDisp, labelWidth)}  {val}");
            }
            else
            {
                Console.WriteLine($"{i + 1,3}  {s}  {PadColumn(keyDisp, keyWidth)}  {PadColumn(labelDisp, labelWidth)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Commands: a|add, c|copy <n> [m], d|delete <n> [m], e|edit <n>, g|grep [regex], h|help, json <sep>, yaml <sep>, l|label [value], o|open, p|prefix [value], q|quit, r|reload, s|save, u|undo <n> [m]|all, w|whoami");
    }

    private void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Help - Commands");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine("a|add            Add a new key under the current prefix");
        Console.WriteLine("c|copy <n> [m]   Copy rows n..m to another label and switch");
        Console.WriteLine("d|delete <n> [m] Delete items n..m");
        Console.WriteLine("e|edit <n>       Edit value of item number n");
        Console.WriteLine("g|grep [regex]   Set key regex filter (no arg clears)");
        Console.WriteLine("h|help|?         Show this help");
        Console.WriteLine("json <sep>       Edit visible items as nested JSON split by <sep>");
        Console.WriteLine("yaml <sep>       Edit visible items as nested YAML split by <sep>");
        Console.WriteLine("o|open           Edit all visible items in external editor");
        Console.WriteLine("p|prefix [value] Change prefix (no arg prompts)");
        Console.WriteLine("l|label [value]  Change label filter (no arg clears; '-' = empty label)");
        Console.WriteLine("q|quit           Quit the editor");
        Console.WriteLine("r|reload         Reload from Azure and reconcile local changes");
        Console.WriteLine("s|save           Save all pending changes to Azure");
        Console.WriteLine("u|undo <n> [m]|all  Undo local changes for rows n..m, or 'all' to undo everything");
        Console.WriteLine("w|whoami         Show current identity and endpoint");
        Console.WriteLine();
        Console.WriteLine("Press Enter to return to the list...");
        Console.ReadLine();
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

    private static string PadColumn(string text, int width)
    {
        var t = TextTruncation.TruncateFixed(text, width);
        if (t.Length < width) return t.PadRight(width);
        return t;
    }
}
