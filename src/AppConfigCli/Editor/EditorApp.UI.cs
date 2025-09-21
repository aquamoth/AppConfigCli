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
        var width = GetWindowWidth();
        var height = GetWindowHeight();
        bool includeValue = width >= 60; // minimal width for value column
        var visible = GetVisibleItems();

        // Build header lines for filters and compute paging height accordingly
        var headerLines = BuildFilterHeaderLines(width);
        ComputePaging(height, visible.Count, headerLines.Count, out var pageSize, out var pageCount);
        if (_pageIndex >= pageCount) _pageIndex = Math.Max(0, pageCount - 1);
        if (_pageIndex < 0) _pageIndex = 0;
        var pageText = pageCount > 1 ? $"PAGE {_pageIndex + 1}/{pageCount}" : string.Empty;

        var title = "Azure App Configuration Editor";
        if (!string.IsNullOrEmpty(pageText))
        {
            int pad = Math.Max(1, width - title.Length - pageText.Length);
            Console.WriteLine(title + new string(' ', pad) + pageText);
        }
        else
        {
            Console.WriteLine(title);
        }

        // Render header rows under the title with colored values
        RenderFilterHeader(width);

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

        int start = 0;
        int count = visible.Count;
        if (pageCount > 1)
        {
            start = _pageIndex * pageSize;
            count = Math.Min(pageSize, Math.Max(0, visible.Count - start));
        }
        for (int i = 0; i < count; i++)
        {
            var item = visible[start + i];
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
            Console.Write($"{start + i + 1,3}  {s}  ");
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

        // Single-line prompt hint to avoid wrapping on narrow consoles
        Console.Write("Command (h for help)> ");
    }

    private static int GetWindowHeight()
    {
        try
        {
            var h = Console.WindowHeight;
            return Math.Max(10, Math.Min(h, 200));
        }
        catch
        {
            return 40;
        }
    }

    private void ComputePaging(int windowHeight, int totalItems, int headerLinesCount, out int pageSize, out int pageCount)
    {
        // Reserve lines: title (1) + header filters (variable) + separators/header row (3) + prompt (1)
        int reserved = 1 + headerLinesCount + 3 + 1;
        int maxRows = Math.Max(1, windowHeight - reserved);
        pageSize = maxRows;
        pageCount = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
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

    // Build filter header lines with alignment rules and conditional visibility
    private List<string> BuildFilterHeaderLines(int width)
    {
        var lines = new List<string>();

        // Determine active filters
        string? p = string.IsNullOrWhiteSpace(Prefix) ? null : $"Prefix: {Prefix}";
        string? l = Label is null ? null : $"Label: {(Label.Length == 0 ? "(none)" : Label)}";
        string? f = string.IsNullOrEmpty(KeyRegexPattern) ? null : $"Filter: {KeyRegexPattern}";

        // Nothing to render
        if (p is null && l is null && f is null) return lines;

        static string ComposeLine(int width, params (int pos, string text)[] segments)
        {
            var arr = new char[width];
            for (int i = 0; i < width; i++) arr[i] = ' ';
            foreach (var seg in segments)
            {
                if (seg.pos < 0 || seg.pos >= width) continue;
                var text = seg.text;
                int len = Math.Min(text.Length, Math.Max(0, width - seg.pos));
                for (int i = 0; i < len; i++) arr[seg.pos + i] = text[i];
            }
            return new string(arr);
        }

        const int gap = 2;

        static string ComposeCentered(int width, string text)
        {
            int start = Math.Max(0, (width - text.Length) / 2);
            return ComposeLine(width, (start, text));
        }

        // Helper to try three-in-one line layout
        bool TryAllThree(string pp, string ll, string ff, out string? line)
        {
            line = null;
            int pLen = pp.Length, lLen = ll.Length, fLen = ff.Length;
            if (pLen + gap + fLen > width) return false; // impossible to fit left+right
            int rightStart = width - fLen;
            int leftEnd = pLen;
            int centerStart = Math.Max(leftEnd + gap, (width - lLen) / 2);
            int centerEnd = centerStart + lLen;
            if (centerEnd + gap > rightStart) return false; // overlaps
            line = ComposeLine(width, (0, pp), (centerStart, ll), (rightStart, ff));
            return true;
        }

        // Helper to try two on one line (left/right)
        bool TryLeftRight(string left, string right, out string? line)
        {
            line = null;
            if (left.Length + gap + right.Length > width) return false;
            int rightStart = width - right.Length;
            if (rightStart < left.Length + gap) return false;
            line = ComposeLine(width, (0, left), (rightStart, right));
            return true;
        }

        // Layout depending on which are present
        if (p is not null && l is not null && f is not null)
        {
            if (TryAllThree(p, l, f, out var lineA))
            {
                lines.Add(lineA!);
            }
            else if (TryLeftRight(p, l, out var lineB))
            {
                lines.Add(lineB!);
                lines.Add(ComposeLine(width, (0, f)));
            }
            else if (TryLeftRight(l, f, out var lineC))
            {
                lines.Add(ComposeLine(width, (0, p)));
                lines.Add(lineC!);
            }
            else
            {
                lines.Add(ComposeLine(width, (0, p)));
                lines.Add(ComposeLine(width, (0, l)));
                lines.Add(ComposeLine(width, (0, f)));
            }
        }
        else
        {
            // At most two present; try to place on one line, else split
            var present = new List<string>();
            if (p is not null) present.Add(p);
            if (l is not null) present.Add(l);
            if (f is not null) present.Add(f);

            if (present.Count == 1)
            {
                // If the only item is the Label (and prefix is hidden), center it to avoid jumpiness
                if (p is null && l is not null && present[0] == l)
                    lines.Add(ComposeCentered(width, l));
                else
                    lines.Add(ComposeLine(width, (0, present[0])));
            }
            else if (present.Count == 2)
            {
                // Special handling: prefix hidden, label+filter present -> center label, filter right if possible
                if (p is null && l is not null && f is not null)
                {
                    int fStart = width - f.Length;
                    int lStart = Math.Max(0, (width - l.Length) / 2);
                    // Ensure at least gap separation; if overlap, fall back to left/right heuristics
                    if (lStart + l.Length + gap <= fStart)
                    {
                        lines.Add(ComposeLine(width, (lStart, l), (fStart, f)));
                    }
                    else if (TryLeftRight(l, f, out var lineLF))
                    {
                        lines.Add(lineLF!);
                    }
                    else if (TryLeftRight(f, l, out var lineFL))
                    {
                        lines.Add(lineFL!);
                    }
                    else
                    {
                        lines.Add(ComposeLine(width, (0, l)));
                        lines.Add(ComposeLine(width, (0, f)));
                    }
                }
                else
                {
                    var a = present[0];
                    var b = present[1];
                    if (TryLeftRight(a, b, out var lineD)) lines.Add(lineD!);
                    else if (TryLeftRight(b, a, out var lineE)) lines.Add(lineE!);
                    else { lines.Add(ComposeLine(width, (0, a))); lines.Add(ComposeLine(width, (0, b))); }
                }
            }
        }

        return lines;
    }

    // Expose header line count for paging during prompt PageUp/PageDown
    internal int GetHeaderLineCountForWidth(int width)
        => BuildFilterHeaderLines(width).Count;

    private void WriteColored(string text)
    {
        var prev = Console.ForegroundColor;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var color = ClassifyColor(ch);
            if (Console.ForegroundColor != color) Console.ForegroundColor = color;
            Console.Write(ch);
        }
        if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;
    }

    private void RenderFilterHeader(int width)
    {
        // Determine active filters
        string? p = string.IsNullOrWhiteSpace(Prefix) ? null : $"Prefix: {Prefix}";
        string? l = Label is null ? null : $"Label: {(Label.Length == 0 ? "(none)" : Label)}";
        string? f = string.IsNullOrEmpty(KeyRegexPattern) ? null : $"Filter: {KeyRegexPattern}";

        if (p is null && l is null && f is null) return;

        int startTop;
        try { startTop = Console.CursorTop; }
        catch { startTop = 1; }

        const int gap = 2;

        // Locals for rendering one line composed of segments at positions
        void RenderLine(int top, params (int pos, string text)[] segments)
        {
            // Clear the line area first
            try
            {
                Console.SetCursorPosition(0, top);
                Console.Write(new string(' ', Math.Max(0, width)));
            }
            catch { }
            foreach (var seg in segments)
            {
                if (seg.pos < 0 || seg.pos >= width) continue;
                try { Console.SetCursorPosition(seg.pos, top); }
                catch { }
                // Split label/value at first ": "
                var text = seg.text;
                int idx = text.IndexOf(": ", StringComparison.Ordinal);
                if (idx >= 0 && Theme.Enabled)
                {
                    Console.Write(text.Substring(0, idx + 2));
                    var val = text.Substring(idx + 2);
                    WriteColored(val);
                }
                else
                {
                    Console.Write(text);
                }
            }
        }

        // Helper to try all 3 on one line (Prefix left, Label center, Filter right)
        bool TryAllThree(string pp, string ll, string ff)
        {
            int pLen = pp.Length, lLen = ll.Length, fLen = ff.Length;
            if (pLen + gap + fLen > width) return false;
            int rightStart = width - fLen;
            int centerStart = Math.Max(pLen + gap, (width - lLen) / 2);
            if (centerStart + lLen + gap > rightStart) return false;
            RenderLine(startTop, (0, pp), (centerStart, ll), (rightStart, ff));
            return true;
        }

        // Helper for two segments left/right on one line
        bool TryLeftRight(int top, string left, string right)
        {
            if (left.Length + gap + right.Length > width) return false;
            int rightStart = width - right.Length;
            if (rightStart < left.Length + gap) return false;
            RenderLine(top, (0, left), (rightStart, right));
            return true;
        }

        int line = 0;
        if (p is not null && l is not null && f is not null)
        {
            if (TryAllThree(p, l, f))
            {
                line += 1; // rendered one line
            }
            else if (TryLeftRight(startTop + line, p, l))
            {
                line += 1;
                RenderLine(startTop + line, (0, f));
                line += 1;
            }
            else if (TryLeftRight(startTop + line, l, f))
            {
                line += 1;
                RenderLine(startTop + line, (0, p));
                line += 1;
            }
            else
            {
                RenderLine(startTop + line, (0, p)); line++;
                RenderLine(startTop + line, (0, l)); line++;
                RenderLine(startTop + line, (0, f)); line++;
            }
        }
        else
        {
            var present = new List<string>();
            if (p is not null) present.Add(p);
            if (l is not null) present.Add(l);
            if (f is not null) present.Add(f);

            if (present.Count == 1)
            {
                if (p is null && l is not null && present[0] == l)
                {
                    int centerStart = Math.Max(0, (width - l.Length) / 2);
                    RenderLine(startTop + line, (centerStart, l));
                    line++;
                }
                else
                {
                    RenderLine(startTop + line, (0, present[0]));
                    line++;
                }
            }
            else if (present.Count == 2)
            {
                if (p is null && l is not null && f is not null)
                {
                    int fStart = width - f.Length;
                    int lStart = Math.Max(0, (width - l.Length) / 2);
                    if (lStart + l.Length + gap <= fStart)
                    {
                        RenderLine(startTop + line, (lStart, l), (fStart, f));
                        line++;
                    }
                    else if (TryLeftRight(startTop + line, l, f))
                    {
                        line++;
                    }
                    else if (TryLeftRight(startTop + line, f, l))
                    {
                        line++;
                    }
                    else
                    {
                        RenderLine(startTop + line, (0, l)); line++;
                        RenderLine(startTop + line, (0, f)); line++;
                    }
                }
                else
                {
                    var a = present[0];
                    var b = present[1];
                    if (TryLeftRight(startTop + line, a, b)) line++;
                    else if (TryLeftRight(startTop + line, b, a)) line++;
                    else { RenderLine(startTop + line, (0, a)); line++; RenderLine(startTop + line, (0, b)); line++; }
                }
            }
        }

        // After rendering, position cursor at the start of the next line so subsequent
        // Console.WriteLine writes begin on a fresh line even if we wrote with SetCursorPosition.
        try
        {
            int renderedLines = BuildFilterHeaderLines(width).Count;
            Console.SetCursorPosition(0, startTop + renderedLines);
        }
        catch { }
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
    internal static ConsoleColor ClassifyColorFor(ConsoleTheme theme, char ch)
    {
        if (ch == '…') return theme.Default; // keep ellipsis neutral
        if (char.IsDigit(ch)) return theme.Number;
        if (ControlChars.Contains(ch) || char.IsPunctuation(ch)) return theme.Control;
        if (char.IsLetter(ch)) return theme.Letters;
        return theme.Default; // includes whitespace
    }
}
