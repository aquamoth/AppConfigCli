using AppConfigCli.Core;
using AppConfigCli.Core.UI;
using AppConfigCli.Editor.Abstractions;
using Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AppConfigCli;

internal sealed class EditorApp
{
    // Define which characters count as "control" for highlighting
    private static readonly HashSet<char> ControlChars = [.. ",.-[]{}!:/\\()@#$%^&*+=?|<>;'\"_".ToCharArray()];

    private readonly IConfigRepository _repo;
    internal readonly Func<Task>? WhoAmI;

    // Paging state
    private int _pageIndex = 0;

    // Cached prefix candidates built from all repository keys plus current in-memory items
    private List<string>? _prefixCache;

    internal string? Prefix { get; set; }
    internal string? Label { get; set; }
    internal List<Item> Items { get; } = [];
    internal string? KeyRegexPattern { get; set; }
    internal Regex? KeyRegex { get; set; }
    internal Regex? ValueHighlightRegex { get; set; }
    internal IFileSystem Filesystem { get; init; }
    internal IExternalEditor ExternalEditor { get; init; }
    internal ConsoleTheme Theme { get; init; }
    internal List<string> CommandHistory { get; } = [];

    // Test-only hooks for integration tests (internal visibility)
    internal List<Item> Test_Items => Items;
    internal Task Test_SaveAsync() => SaveAsync(pause: false);

    public EditorApp(
        IConfigRepository repo,
        string? prefix,
        string? label,
        Func<Task> whoAmI,
        IFileSystem fs,
        IExternalEditor externalEditor,
        ConsoleTheme theme,
        IConsoleEx consoleEx)
    {
        _repo = repo;
        Prefix = prefix;
        Label = label;
        WhoAmI = whoAmI;
        Filesystem = fs;
        ExternalEditor = externalEditor;
        Theme = theme;
        ConsoleEx = consoleEx;
    }

    internal IConsoleEx ConsoleEx { get; init; }

    private void Render()
    {
        ConsoleEx.Clear();
        var width = GetWindowWidth();
        var height = GetWindowHeight();
        bool includeValue = width >= 60; // minimal width for value column
        var visible = GetVisibleItems();

        // Compute header line count via shared layout service (no rendering here)
        int headerLineCount = GetHeaderLineCountForWidth(width);
        ComputePaging(height, visible.Count, headerLineCount, out var pageSize, out var pageCount);
        if (_pageIndex >= pageCount) _pageIndex = Math.Max(0, pageCount - 1);
        if (_pageIndex < 0) _pageIndex = 0;
        var pageText = pageCount > 1 ? $"PAGE {_pageIndex + 1}/{pageCount}" : string.Empty;

        var title = "Azure App Configuration Editor";
        if (!string.IsNullOrEmpty(pageText))
        {
            int pad = Math.Max(1, width - title.Length - pageText.Length);
            ConsoleEx.WriteLine(title + new string(' ', pad) + pageText);
        }
        else
        {
            ConsoleEx.WriteLine(title);
        }

        // Render header rows using shared layout + console wrapper
        RenderHeaderViaLayout(width);

        // Determine the current page slice before computing dynamic widths
        int pageStart = 0;
        int pageCountItems = visible.Count;
        if (pageCount > 1)
        {
            pageStart = _pageIndex * pageSize;
            pageCountItems = Math.Min(pageSize, Math.Max(0, visible.Count - pageStart));
        }

        // Prepare subset for the current page to compute dynamic widths
        var pageItems = visible.Skip(pageStart).Take(pageCountItems).ToList();
        var layoutMapper = new EditorMappers();
        var coreVisible = pageItems.Select(layoutMapper.ToCoreItem).ToList();
        bool showLabelColumn = Label is null; // Hide label column when label filter is active

        // Dynamic index width based on the largest visible index on this page
        int maxIndexValue = pageStart + pageCountItems; // global numbering
        int indexDigits = Math.Max(1, maxIndexValue.ToString().Length);

        // Compute longest lengths on this page
        int longestKeyLen = pageItems.Select(i => i.ShortKey.Length).DefaultIfEmpty(1).Max();
        int longestLabelLen = showLabelColumn ? pageItems.Select(i => (i.Label ?? "(none)").Length).DefaultIfEmpty(1).Max() : 0;
        int longestValueLen = includeValue ? pageItems.Select(i => (i.Value ?? string.Empty).Replace('\n', ' ').Length).DefaultIfEmpty(1).Max() : 0;

        // Allocate widths so that index fits, status is 1 char, and columns share the rest
        const int minKey = 15;
        const int maxKey = 80;
        const int minLabel = 6;
        const int maxLabel = 30;
        const int minValue = 10;

        int sepKey = 1; // one space after status before key
        int sepLabel = showLabelColumn ? 2 : 0; // between key and label
        int sepValue = includeValue ? 2 : 0; // before value

        int nonColumn = indexDigits + 1 /*status*/ + sepKey + sepLabel + sepValue;
        int availableCols = Math.Max(0, width - nonColumn);

        int labelWidth = 0;
        if (showLabelColumn)
        {
            labelWidth = Math.Clamp(longestLabelLen, minLabel, Math.Min(maxLabel, availableCols));
            availableCols = Math.Max(0, availableCols - labelWidth);
        }

        int keyWidth, valueWidth;
        if (includeValue)
        {
            int keyNeeded = Math.Clamp(longestKeyLen, minKey, maxKey);
            int valueNeeded = Math.Max(minValue, longestValueLen);

            if (availableCols >= keyNeeded + valueNeeded)
            {
                // Everything fits fully; give all extra space to value
                keyWidth = keyNeeded;
                valueWidth = Math.Max(1, availableCols - keyWidth);
            }
            else
            {
                // Not enough for both fully — cap key at needed and reserve at least min for value
                int maxKeyGivenValueMin = Math.Max(1, availableCols - minValue);
                keyWidth = Math.Min(keyNeeded, maxKeyGivenValueMin);
                valueWidth = Math.Max(1, availableCols - keyWidth);
            }
        }
        else
        {
            keyWidth = Math.Clamp(longestKeyLen, minKey, availableCols);
            valueWidth = 0;
        }

        // (Old static layout logic removed; using dynamic per-page widths computed above)

        ConsoleEx.WriteLine(new string('-', width));
        var idxHeader = "Idx".PadLeft(indexDigits);
        var header = new System.Text.StringBuilder();
        header.Append(idxHeader);
        header.Append(' '); // status slot
        header.Append(' '); // space before key
        header.Append(PadColumn("Key", keyWidth));
        if (showLabelColumn)
        {
            header.Append("  ");
            header.Append(PadColumn("Label", labelWidth));
        }
        if (valueWidth > 0)
        {
            header.Append("  ");
            header.Append("Value");
        }
        ConsoleEx.WriteLine(header.ToString());
        ConsoleEx.WriteLine(new string('-', width));

        for (int i = 0; i < pageCountItems; i++)
        {
            var item = visible[pageStart + i];
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

            // Left prefix: index (dynamic width), status (1 char), then a space
            ConsoleEx.ForegroundColor = Theme.Default;
            var idxText = (pageStart + i + 1).ToString().PadLeft(indexDigits);
            bool isDirty = s != ' ';
            if (Theme.Enabled && isDirty)
            {
                var prev = ConsoleEx.ForegroundColor;
                var col = ClassifyColor(s); // use same color as status indicator
                if (ConsoleEx.ForegroundColor != col) ConsoleEx.ForegroundColor = col;
                ConsoleEx.Write(idxText);
                if (ConsoleEx.ForegroundColor != prev) ConsoleEx.ForegroundColor = prev;
            }
            else
            {
                ConsoleEx.Write(idxText);
            }
            if (Theme.Enabled)
            {
                var prev = ConsoleEx.ForegroundColor;
                var col = ClassifyColor(s);
                if (ConsoleEx.ForegroundColor != col) ConsoleEx.ForegroundColor = col;
                ConsoleEx.Write(s);
                if (ConsoleEx.ForegroundColor != Theme.Default) ConsoleEx.ForegroundColor = Theme.Default;
            }
            else
            {
                ConsoleEx.Write(s);
            }
            ConsoleEx.Write(' ');
            // Key (colored)
            if (Theme.Enabled) WriteColoredFixed(keyDisp, keyWidth); else ConsoleEx.Write(PadColumn(keyDisp, keyWidth));
            ConsoleEx.ForegroundColor = Theme.Default;
            if (showLabelColumn)
            {
                ConsoleEx.Write("  ");
                // Label (unstyled)
                ConsoleEx.Write(PadColumn(labelDisp, labelWidth));
            }

            if (valueWidth > 0)
            {
                ConsoleEx.Write("  ");
                if (Theme.Enabled)
                {
                    if (ValueHighlightRegex is not null)
                        WriteColoredFixedWithHighlight(valDisp, valueWidth, ValueHighlightRegex);
                    else
                        WriteColoredFixed(valDisp, valueWidth);
                }
                else
                {
                    ConsoleEx.Write(PadColumn(valDisp, valueWidth));
                }
            }
            ConsoleEx.ForegroundColor = Theme.Default;
            ConsoleEx.WriteLine("");
        }

        // Single-line prompt hint to avoid wrapping on narrow consoles
        ConsoleEx.Write("Command (h for help)> ");
    }

    // Expose a safe repaint for commands needing to trigger immediate UI refresh
    internal void Repaint()
    {
        try { Render(); } catch { }
    }

    private int GetWindowHeight()
    {
        try
        {
            var h = ConsoleEx.WindowHeight;
            return Math.Max(10, h);
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

    private int GetWindowWidth()
    {
        try
        {
            var w = ConsoleEx.WindowWidth;
            // Allow narrow widths; we handle hiding columns below thresholds
            return Math.Max(20, w);
        }
        catch
        {
            return 100;
        }
    }

    // Expose header line count for paging during prompt PageUp/PageDown
    internal int GetHeaderLineCountForWidth(int width)
    {
        string? p = string.IsNullOrWhiteSpace(Prefix) ? null : $"Prefix: {Prefix}";
        string? l = Label is null ? null : $"Label: {(Label.Length == 0 ? "(none)" : Label)}";
        string? f = string.IsNullOrEmpty(KeyRegexPattern) ? null : $"Filter: {KeyRegexPattern}";
        return HeaderLayout.Compute(width, p, l, f).Count;
    }

    private void WriteColored(string text)
    {
        var prev = ConsoleEx.ForegroundColor;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var color = ClassifyColor(ch);
            if (ConsoleEx.ForegroundColor != color) ConsoleEx.ForegroundColor = color;
            ConsoleEx.Write(ch);
        }
        if (ConsoleEx.ForegroundColor != prev) ConsoleEx.ForegroundColor = prev;
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
        var prev = ConsoleEx.ForegroundColor;
        for (int i = 0; i < len; i++)
        {
            var ch = text[i];
            var color = ClassifyColor(ch);
            if (ConsoleEx.ForegroundColor != color) ConsoleEx.ForegroundColor = color;
            ConsoleEx.Write(ch);
        }
        if (len < width)
        {
            if (ConsoleEx.ForegroundColor != Theme.Default) ConsoleEx.ForegroundColor = Theme.Default;
            ConsoleEx.Write(new string(' ', width - len));
        }
        if (ConsoleEx.ForegroundColor != prev) ConsoleEx.ForegroundColor = prev;
    }

    private void WriteColoredFixedWithHighlight(string text, int width, System.Text.RegularExpressions.Regex highlight)
    {
        int len = Math.Min(text.Length, width);
        var prevFg = ConsoleEx.ForegroundColor;
        var prevBg = ConsoleEx.BackgroundColor;

        // Precompute highlight flags for displayed substring
        var flags = new bool[len];
        try
        {
            var m = highlight.Matches(text.Substring(0, len));
            foreach (System.Text.RegularExpressions.Match match in m)
            {
                int start = Math.Max(0, match.Index);
                int end = Math.Min(len, match.Index + match.Length);
                for (int i = start; i < end; i++) flags[i] = true;
            }
        }
        catch { }

        for (int i = 0; i < len; i++)
        {
            var ch = text[i];
            var fg = ClassifyColor(ch);
            bool hl = flags[i];
            if (hl)
            {
                if (ConsoleEx.BackgroundColor != ConsoleColor.DarkYellow) ConsoleEx.BackgroundColor = ConsoleColor.DarkYellow;
                if (ConsoleEx.ForegroundColor != ConsoleColor.Black) ConsoleEx.ForegroundColor = ConsoleColor.Black;
            }
            else
            {
                if (ConsoleEx.BackgroundColor != prevBg) ConsoleEx.BackgroundColor = prevBg;
                if (ConsoleEx.ForegroundColor != fg) ConsoleEx.ForegroundColor = fg;
            }
            ConsoleEx.Write(ch);
        }
        // Reset colors and pad if needed
        if (ConsoleEx.BackgroundColor != prevBg) ConsoleEx.BackgroundColor = prevBg;
        if (ConsoleEx.ForegroundColor != prevFg) ConsoleEx.ForegroundColor = prevFg;
        if (len < width)
        {
            ConsoleEx.Write(new string(' ', width - len));
        }
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

    private void RenderHeaderViaLayout(int width)
    {
        string? p = string.IsNullOrWhiteSpace(Prefix) ? null : $"Prefix: {Prefix}";
        string? l = Label is null ? null : $"Label: {(Label.Length == 0 ? "(none)" : Label)}";
        string? f = string.IsNullOrEmpty(KeyRegexPattern) ? null : $"Filter: {KeyRegexPattern}";
        if (p is null && l is null && f is null) return;
        int startTop; try { startTop = ConsoleEx.CursorTop; } catch { startTop = 1; }
        var layout = HeaderLayout.Compute(width, p, l, f);
        void RenderLine(int top, System.Collections.Generic.List<HeaderLayout.Segment> segs)
        {
            ConsoleEx.SetCursorPosition(0, top);
            ConsoleEx.Write(new string(' ', Math.Max(0, width)));
            foreach (var seg in segs)
            {
                if (seg.Pos < 0 || seg.Pos >= width) continue;
                ConsoleEx.SetCursorPosition(seg.Pos, top);
                var text = seg.Text;
                int idx = text.IndexOf(": ", System.StringComparison.Ordinal);
                if (idx >= 0 && Theme.Enabled)
                {
                    ConsoleEx.Write(text.Substring(0, idx + 2));
                    var val = text.Substring(idx + 2);
                    WriteColored(val);
                }
                else
                {
                    ConsoleEx.Write(text);
                }
            }
        }
        int line = 0;
        foreach (var segs in layout)
        {
            RenderLine(startTop + line, segs);
            line++;
        }
        ConsoleEx.SetCursorPosition(0, startTop + line);
    }

    private void PageUp()
    {
        var total = GetVisibleItems().Count;
        int pageSize, pageCount;
        try { int h = ConsoleEx.WindowHeight; int w = ConsoleEx.WindowWidth; ComputePaging(h, total, GetHeaderLineCountForWidth(Math.Max(20, Math.Min(w, 240))), out pageSize, out pageCount); }
        catch { ComputePaging(40, total, GetHeaderLineCountForWidth(100), out pageSize, out pageCount); }
        if (pageCount <= 1) { _pageIndex = 0; return; }
        _pageIndex = Math.Max(0, _pageIndex - 1);
    }

    private void PageDown()
    {
        var total = GetVisibleItems().Count;
        int pageSize, pageCount;
        try { int h = ConsoleEx.WindowHeight; int w = ConsoleEx.WindowWidth; ComputePaging(h, total, GetHeaderLineCountForWidth(Math.Max(20, Math.Min(w, 240))), out pageSize, out pageCount); }
        catch { ComputePaging(40, total, GetHeaderLineCountForWidth(100), out pageSize, out pageCount); }
        if (pageCount <= 1) { _pageIndex = 0; return; }
        _pageIndex = Math.Min(pageCount - 1, _pageIndex + 1);
    }

    // Allow commands to trigger paging during custom prompts
    internal void PageUpCommand() => PageUp();
    internal void PageDownCommand() => PageDown();

    public async Task LoadAsync()
    {
        // Build server snapshot
        var server = (await _repo.ListAsync(Prefix, Label)).ToList();

        // Map local items to Core using Mapperly
        var mapper = new EditorMappers();
        var local = Items.Select(mapper.ToCoreItem).ToList();

        var reconciler = new AppStateReconciler();
        var freshCore = reconciler.Reconcile(Prefix ?? string.Empty, Label, local, server);

        Items.Clear();
        foreach (var it in freshCore)
        {
            Items.Add(mapper.ToUiItem(it));
        }
    }

    public async Task RunAsync()
    {
        var prevTreatCtrlC = ConsoleEx.TreatControlCAsInput;
        ConsoleEx.TreatControlCAsInput = true;
        try
        {
            while (true)
            {
                Render();
                var (ctrlC, input) = ReadLineOrCtrlC_Engine(
                    CommandHistory,
                    onRepaint: () =>
                    {
                        Render();
                        return (ConsoleEx.CursorLeft, ConsoleEx.CursorTop);
                    },
                    onPageUp: () => PageUp(),
                    onPageDown: () => PageDown());
                if (ctrlC)
                {
                    var quit = new Editor.Commands.Quit();
                    var shouldExit = await quit.TryQuitAsync(this);
                    if (shouldExit) return;
                    // back to main screen
                    continue;
                }
                if (input is null) continue;
                if (!CommandParser.TryParse(input, out var cmd, out var err) || cmd is null)
                {
                    if (!string.IsNullOrEmpty(err))
                    {
                        ConsoleEx.WriteLine(err);
                        ConsoleEx.WriteLine("Press Enter to continue...");
                        ConsoleEx.ReadLine();
                    }
                    continue;
                }
                var result = await cmd.ExecuteAsync(this);
                // Add executed command to history unless it is a duplicate of the last entry
                if (!string.IsNullOrWhiteSpace(input))
                {
                    var last = CommandHistory.Count > 0 ? CommandHistory[^1] : null;
                    if (!string.Equals(last, input, StringComparison.Ordinal))
                    {
                        CommandHistory.Add(input);
                    }
                }
                if (result.ShouldExit) return;
            }
        }
        finally
        {
            ConsoleEx.TreatControlCAsInput = prevTreatCtrlC;
        }
    }

    // Simpler input reader for command prompts: supports ESC/Ctrl+C cancel, PageUp/PageDown, full cursor/word ops, and viewport ellipses
    internal (bool Cancelled, string? Text) ReadLineWithPagingCancelable(
        Func<(int Left, int Top)> onRepaint,
        Action onPageUp,
        Action onPageDown,
        string? initial = null)
    {
        var engine = new LineEditorEngine();
        engine.SetInitial(initial ?? string.Empty);
        int startLeft, startTop;
        startLeft = ConsoleEx.CursorLeft; startTop = ConsoleEx.CursorTop;

        void Render()
        {
            int winWidth = ConsoleEx.WindowWidth;
            int contentWidth = Math.Max(1, winWidth - startLeft - 1);
            engine.EnsureVisible(contentWidth);
            var view = engine.GetView(contentWidth);
            ConsoleEx.SetCursorPosition(startLeft, startTop);
            int vlen = Math.Min(view.Length, contentWidth);
            if (vlen > 0) ConsoleEx.Write(view[..vlen]);
            if (vlen < contentWidth) ConsoleEx.Write(new string(' ', contentWidth - vlen));
            int cursorCol = startLeft + Math.Min(engine.Cursor - engine.ScrollStart, contentWidth - 1);
            int safeCol = Math.Min(Math.Max(0, winWidth - 1), Math.Max(0, cursorCol));
            ConsoleEx.SetCursorPosition(safeCol, startTop);
        }

        Render();
        while (true)
        {
            ConsoleKeyInfo key;
            if (ConsoleEx.KeyAvailable) key = ConsoleEx.ReadKey(intercept: true); else { try { System.Threading.Thread.Sleep(25); } catch { } continue; }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C) { ConsoleEx.WriteLine(""); return (true, null); }
            if (key.Key == ConsoleKey.Escape) { ConsoleEx.WriteLine(""); return (true, null); }

            if (key.Key == ConsoleKey.PageUp || key.Key == ConsoleKey.PageDown)
            {
                try { if (key.Key == ConsoleKey.PageUp) onPageUp(); else onPageDown(); } catch { }
                try { var pos = onRepaint(); startLeft = pos.Left; startTop = pos.Top; } catch { }
                Render();
                continue;
            }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow) { engine.CtrlWordLeft(); Render(); continue; }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow) { engine.CtrlWordRight(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Backspace) { engine.CtrlWordBackspace(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Delete) { engine.CtrlWordDelete(); Render(); continue; }

            if (key.Key == ConsoleKey.LeftArrow) { engine.Left(); Render(); continue; }
            if (key.Key == ConsoleKey.RightArrow) { engine.Right(); Render(); continue; }
            if (key.Key == ConsoleKey.Home) { engine.Home(); Render(); continue; }
            if (key.Key == ConsoleKey.End) { engine.End(); Render(); continue; }

            if (key.Key == ConsoleKey.Enter) { ConsoleEx.WriteLine(""); return (false, engine.Buffer.ToString()); }
            if (key.Key == ConsoleKey.Backspace) { engine.Backspace(); Render(); continue; }
            if (key.Key == ConsoleKey.Delete) { engine.Delete(); Render(); continue; }
            if (!char.IsControl(key.KeyChar)) { engine.Insert(key.KeyChar); Render(); continue; }
        }
    }

    // Engine-backed line editor for the main prompt with history + viewport + paging
    internal (bool CtrlC, string? Text) ReadLineOrCtrlC_Engine(
        List<string>? history = null,
        Func<(int Left, int Top)>? onRepaint = null,
        Action? onPageUp = null,
        Action? onPageDown = null)
    {
        var engine = new LineEditorEngine();
        engine.SetInitial(string.Empty);
        int startLeft, startTop;
        startLeft = ConsoleEx.CursorLeft; startTop = ConsoleEx.CursorTop;
        int lastW = ConsoleEx.WindowWidth, lastH = ConsoleEx.WindowHeight;

        int histIndex = history?.Count ?? 0; // bottom slot
        string draft = string.Empty;
        bool modifiedFromHistory = false;

        try
        {
            int avail = Math.Max(0, ConsoleEx.WindowWidth - startLeft - 1);
            if (avail < 10) { ConsoleEx.WriteLine(); startLeft = 0; startTop = ConsoleEx.CursorTop; }
        }
        catch { }

        void Render()
        {
            int w = ConsoleEx.WindowWidth;
            int content = Math.Max(1, w - startLeft - 1);
            engine.EnsureVisible(content);
            var view = engine.GetView(content);
            try
            {
                ConsoleEx.SetCursorPosition(startLeft, startTop);
                int vlen = Math.Min(view.Length, content);
                if (vlen > 0) ConsoleEx.Write(view[..vlen]);
                if (vlen < content) ConsoleEx.Write(new string(' ', content - vlen));
                int cursorCol = startLeft + Math.Min(engine.Cursor - engine.ScrollStart, content - 1);
                int safeCol = Math.Min(Math.Max(0, w - 1), Math.Max(0, cursorCol));
                ConsoleEx.SetCursorPosition(safeCol, startTop);
            }
            catch { }
        }

        Render();
        while (true)
        {
            // Resize repaint
            try
            {
                int w = ConsoleEx.WindowWidth, h = ConsoleEx.WindowHeight;
                if ((w != lastW || h != lastH) && onRepaint is not null)
                {
                    lastW = w; lastH = h;
                    var pos = onRepaint();
                    startLeft = pos.Left; startTop = pos.Top;
                }
            }
            catch { }

            ConsoleKeyInfo key;
            if (ConsoleEx.KeyAvailable) key = ConsoleEx.ReadKey(intercept: true); else { try { System.Threading.Thread.Sleep(50); } catch { } continue; }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C) { ConsoleEx.WriteLine(""); return (true, null); }
            if (key.Key == ConsoleKey.Enter) { ConsoleEx.WriteLine(""); return (false, engine.Buffer.ToString()); }

            if (key.Key == ConsoleKey.PageUp || key.Key == ConsoleKey.PageDown)
            {
                try
                {
                    if (key.Key == ConsoleKey.PageUp) onPageUp?.Invoke(); else onPageDown?.Invoke();
                    if (onRepaint is not null) { var pos = onRepaint(); startLeft = pos.Left; startTop = pos.Top; }
                }
                catch { }
                Render();
                continue;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                if (histIndex != (history?.Count ?? 0)) histIndex = history?.Count ?? 0;
                engine.SetInitial(string.Empty);
                draft = string.Empty;
                modifiedFromHistory = true;
                Render();
                continue;
            }

            // History navigation
            if (key.Key == ConsoleKey.UpArrow && history is not null)
            {
                if (histIndex > 0)
                {
                    if (histIndex == history.Count) draft = engine.Buffer.ToString();
                    histIndex--;
                    engine.SetInitial(history[histIndex]);
                    modifiedFromHistory = false;
                    Render();
                }
                continue;
            }
            if (key.Key == ConsoleKey.DownArrow && history is not null)
            {
                if (histIndex < history.Count)
                {
                    histIndex++;
                    if (histIndex == history.Count) engine.SetInitial(draft); else engine.SetInitial(history[histIndex]);
                    modifiedFromHistory = false;
                    Render();
                }
                continue;
            }

            void EnsureDraft()
            {
                if (histIndex != (history?.Count ?? 0) && !modifiedFromHistory)
                {
                    modifiedFromHistory = true;
                    draft = engine.Buffer.ToString();
                    histIndex = history?.Count ?? 0;
                }
            }

            // Word ops
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow) { engine.CtrlWordLeft(); Render(); continue; }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow) { engine.CtrlWordRight(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Backspace) { EnsureDraft(); engine.CtrlWordBackspace(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Delete) { EnsureDraft(); engine.CtrlWordDelete(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }

            // Basic nav
            if (key.Key == ConsoleKey.LeftArrow) { engine.Left(); Render(); continue; }
            if (key.Key == ConsoleKey.RightArrow) { engine.Right(); Render(); continue; }
            if (key.Key == ConsoleKey.Home) { engine.Home(); Render(); continue; }
            if (key.Key == ConsoleKey.End) { engine.End(); Render(); continue; }

            // Char edits
            if (key.Key == ConsoleKey.Backspace) { EnsureDraft(); engine.Backspace(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (key.Key == ConsoleKey.Delete) { EnsureDraft(); engine.Delete(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (!char.IsControl(key.KeyChar)) { EnsureDraft(); engine.Insert(key.KeyChar); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
        }
    }

    private static string MakeKey(string fullKey, string? label)
        => fullKey + "\n" + (label ?? string.Empty);

    internal async Task SaveAsync(bool pause = true)
    {
        ConsoleEx.WriteLine("Saving changes...");
        int changes = 0;

        // Compute consolidated change set using Core.ChangeApplier
        var mapper = new EditorMappers();
        var coreItems = Items.Select(mapper.ToCoreItem).ToList();
        var changeSet = AppConfigCli.Core.ChangeApplier.Compute(coreItems);

        // Apply upserts (last-wins per key/label already handled in ChangeApplier)
        foreach (var up in changeSet.Upserts)
        {
            try
            {
                await _repo.UpsertAsync(up);

                // Mark all corresponding UI items as unchanged and sync OriginalValue
                foreach (var it in Items.Where(i =>
                    i.FullKey == up.Key &&
                    string.Equals(AppConfigCli.Core.LabelFilter.ForWrite(i.Label), up.Label, StringComparison.Ordinal)).ToList())
                {
                    it.OriginalValue = it.Value;
                    it.State = ItemState.Unchanged;
                }
                changes++;
            }
            catch (RequestFailedException ex)
            {
                ConsoleEx.WriteLine($"Failed to set '{up.Key}': {ex.Message}");
            }
        }

        // Apply deletions
        foreach (var del in changeSet.Deletes)
        {
            try
            {
                await _repo.DeleteAsync(del.Key, del.Label);
                // Remove only items marked as Deleted for that key/label
                for (int idx = Items.Count - 1; idx >= 0; idx--)
                {
                    var it = Items[idx];
                    if (it.State != ItemState.Deleted) continue;
                    if (it.FullKey != del.Key) continue;
                    if (!string.Equals(AppConfigCli.Core.LabelFilter.ForWrite(it.Label), del.Label, StringComparison.Ordinal)) continue;
                    Items.RemoveAt(idx);
                }
                changes++;
            }
            catch (RequestFailedException ex)
            {
                ConsoleEx.WriteLine($"Failed to delete '{del.Key}': {ex.Message}");
            }
        }

        ConsoleEx.WriteLine(changes == 0 ? "No changes to save." : $"Saved {changes} change(s).");
        if (pause)
        {
            ConsoleEx.WriteLine("Press Enter to continue...");
            ConsoleEx.ReadLine();
        }
    }

    internal bool HasPendingChanges(out int newCount, out int modCount, out int delCount)
    {
        newCount = Items.Count(i => i.State == ItemState.New);
        modCount = Items.Count(i => i.State == ItemState.Modified);
        delCount = Items.Count(i => i.State == ItemState.Deleted);
        return (newCount + modCount + delCount) > 0;
    }

    internal static int CompareItems(Item a, Item b)
    {
        //TODO: This utility function should be moved somewhere else
        int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
        if (c != 0) return c;
        return string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
    }

    internal void ConsolidateDuplicates()
    {
        var groups = Items.GroupBy(i => MakeKey(i.FullKey, i.Label)).ToList();
        foreach (var g in groups)
        {
            if (g.Count() <= 1) continue;
            var keep = g.FirstOrDefault(i => i.State != ItemState.Deleted) ?? g.First();
            foreach (var extra in g)
            {
                if (!ReferenceEquals(extra, keep))
                {
                    Items.Remove(extra);
                }
            }
        }
    }

    internal List<Item> GetVisibleItems()
    {
        // Delegate visibility to Core.ItemFilter to keep semantics centralized
        var mapper = new EditorMappers();
        var coreList = Items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.VisibleIndices(coreList, Label, KeyRegex);
        var result = new List<Item>(indices.Count);
        foreach (var idx in indices)
        {
            result.Add(Items[idx]);
        }
        return result;
    }

    internal List<int>? MapVisibleRangeToItemIndices(int start, int end, out string error)
    {
        // Use Core.ItemFilter to compute indices against a mapped Core list
        var mapper = new EditorMappers();
        var coreList = Items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.MapVisibleRangeToSourceIndices(coreList, Label, KeyRegex, start, end, out error);
        return indices;
    }

    internal void InvalidatePrefixCache()
    {
        _prefixCache = null;
    }

    internal async Task<IReadOnlyList<string>> GetPrefixCandidatesAsync()
    {
        if (_prefixCache is not null)
            return _prefixCache;

        var set = new HashSet<string>(StringComparer.Ordinal);

        // Include in-memory items (unsaved/new)
        foreach (var it in Items)
        {
            if (it.FullKey != null)
            {
                var index = it.FullKey.IndexOf('/');
                if (index > 0) set.Add(it.FullKey[..(index + 1)]);
            }
        }

        // Include all repository entries (ignoring filters)
        //TODO: We could filter by Label, if we invalidate the cache on label change
        var allKeys = await _repo.FetchKeysAsync(prefix: null, labelFilter: null).ConfigureAwait(false);
        foreach (var key in allKeys)
        {
            var index = key.IndexOf('/');
            if (index > 0)
            {
                set.Add(key[..(index + 1)]);
            }
        }

        _prefixCache = [.. set.OrderBy(s => s, StringComparer.Ordinal)];
        return _prefixCache;
    }

    internal bool TryAddPrefixFromKey(string key)
    {
        if (_prefixCache is null)
            return false; // cache not built yet

        if (string.IsNullOrEmpty(key))
            return false; // no key => no prefix

        var keyIndex = key.IndexOf('/');
        if (keyIndex <= 0)
            return false; // no prefix

        var prefix = key[..(keyIndex + 1)];

        var prefixIndex = _prefixCache.BinarySearch(prefix, StringComparer.Ordinal);
        if (prefixIndex >= 0)
            return false; // already present

        // insert prefix in sorted order
        _prefixCache.Insert(~prefixIndex, prefix);
        return true;
    }
}
