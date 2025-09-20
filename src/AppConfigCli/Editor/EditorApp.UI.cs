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
        Console.WriteLine(CommandParser.GetSummaryLine());
    }

    private void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine(CommandParser.GetHelpText());
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
