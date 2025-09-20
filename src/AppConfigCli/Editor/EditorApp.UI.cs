using System;
using System.Collections.Generic;
using System.Linq;
using AppConfigCli.Core.UI;

namespace AppConfigCli;

internal sealed partial class EditorApp
{
    // Define which characters count as "control" for highlighting
    private static readonly System.Collections.Generic.HashSet<char> ControlChars =
        new System.Collections.Generic.HashSet<char>(",.-[]{}!:/\\()@#$%^&*+=?|<>;'\"_".ToCharArray());

    private void Render()
    {
        Console.Clear();
        Console.WriteLine("Azure App Configuration Editor");
        var prefixDisplay = string.IsNullOrWhiteSpace(Prefix) ? "(none)" : Prefix;
        var labelDisplay = Label is null ? "(any)" : (Label.Length == 0 ? "(none)" : Label);
        var keyRegexDisplay = string.IsNullOrEmpty(KeyRegexPattern) ? "(none)" : KeyRegexPattern;
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
            var valFull = (item.Value ?? string.Empty).Replace('\n', ' ');
            var valDisp = valueWidth > 0 ? TextTruncation.TruncateFixed(valFull, valueWidth) : string.Empty;

            // Left prefix: index, state
            Console.ForegroundColor = Theme.Default;
            Console.Write($"{i + 1,3}  {s}  ");
            // Key (colored)
            if (Theme.Enabled) WriteColoredFixed(keyDisp, keyWidth); else Console.Write(PadColumn(keyDisp, keyWidth));
            Console.ForegroundColor = Theme.Default;
            Console.Write("  ");
            // Label (unstyled)
            Console.Write(PadColumn(labelDisp, labelWidth));

            if (valueWidth > 0)
            {
                Console.Write("  ");
                if (Theme.Enabled) WriteColoredFixed(valDisp, valueWidth); else Console.Write(PadColumn(valDisp, valueWidth));
            }
            Console.ForegroundColor = Theme.Default;
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine(CommandParser.GetSummaryLine());
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

    private void WriteColoredFixed(string text, int width)
    {
        // Write text up to width with per-character coloring, then pad to width
        int len = Math.Min(text.Length, width);
        var prev = Console.ForegroundColor;
        for (int i = 0; i < len; i++)
        {
            var ch = text[i];
            var color = ClassifyColor(ch);
            if (Console.ForegroundColor != color) Console.ForegroundColor = color;
            Console.Write(ch);
        }
        if (len < width)
        {
            if (Console.ForegroundColor != Theme.Default) Console.ForegroundColor = Theme.Default;
            Console.Write(new string(' ', width - len));
        }
        if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;
    }

    private ConsoleColor ClassifyColor(char ch)
    {
        if (ch == '…') return Theme.Default; // keep ellipsis neutral
        if (char.IsDigit(ch)) return Theme.Number;
        if (ControlChars.Contains(ch) || char.IsPunctuation(ch)) return Theme.Control;
        if (char.IsLetter(ch)) return Theme.Letters;
        return Theme.Default; // includes whitespace
    }
}
