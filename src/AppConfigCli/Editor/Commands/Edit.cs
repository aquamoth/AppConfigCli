using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace AppConfigCli.Editor.Commands;

internal sealed record Edit(int Index) : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "e", "edit" },
        Summary = "e|edit <n>",
        Usage = "Usage: e|edit <n>",
        Description = "Edit value of item number n",
        Parser = args =>
        {
            if (args.Length >= 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return (true, new Edit(i), null);
            return (false, null, "Usage: e|edit <n>");
        }
    };

    public override Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        // Map 1-based visible index to actual item index
        var indices = app.MapVisibleRangeToItemIndices(Index, Index, out var error);
        if (indices is null || indices.Count != 1)
        {
            app.ConsoleEx.WriteLine(string.IsNullOrEmpty(error) ? "Invalid index." : error);
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
            return Task.FromResult(new CommandResult());
        }
        var idx = indices[0];
        var item = app.Items[idx];
        var label = item.Label ?? "(none)";
        // Colorized header: key colored, label plain
        app.ConsoleEx.Write("Editing '");
        WriteColoredInline(item.ShortKey, app.Theme);
        app.ConsoleEx.Write("' [" + label + "]  (Enter to save)\n");
        app.ConsoleEx.Write("> ");
        var res = app.ReadLineWithPagingCancelable(
            onRepaint: () => { return (app.ConsoleEx.CursorLeft, app.ConsoleEx.CursorTop); },
            onPageUp: () => { },
            onPageDown: () => { },
            initial: item.Value ?? string.Empty);
        var newVal = res.Cancelled ? null : res.Text;
        if (newVal is not null)
        {
            item.Value = newVal;
            if (!item.IsNew && item.Value != item.OriginalValue)
                item.State = ItemState.Modified;
            if (!item.IsNew && item.Value == item.OriginalValue)
                item.State = ItemState.Unchanged;
        }

        return Task.FromResult(new CommandResult());
    }

    private static void WriteColoredInline(string text, ConsoleTheme theme)
    {
        var prev = Console.ForegroundColor;
        foreach (var ch in text)
        {
            var color = EditorApp.ClassifyColorFor(theme, ch);
            if (Console.ForegroundColor != color) Console.ForegroundColor = color;
            Console.Write(ch);
        }
        if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;
    }

    internal static string? ReadLineWithInitial(string initial, ConsoleTheme theme)
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

            // Render view padded to the full content width to clear remnants, with per-char colors
            Console.SetCursorPosition(startLeft, startTop);
            var prev = Console.ForegroundColor;
            int vlen = Math.Min(view.Length, contentWidth);
            for (int i = 0; i < vlen; i++)
            {
                var ch = view[i];
                var color = EditorApp.ClassifyColorFor(theme, ch);
                if (Console.ForegroundColor != color) Console.ForegroundColor = color;
                Console.Write(ch);
            }
            if (vlen < contentWidth)
            {
                if (Console.ForegroundColor != theme.Default) Console.ForegroundColor = theme.Default;
                Console.Write(new string(' ', contentWidth - vlen));
            }
            if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;

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
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
            {
                Console.WriteLine();
                return null; // cancel like ESC
            }
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null; // cancel editing; caller will not apply changes
            }
            else if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow)
            {
                if (cursor > 0)
                {
                    int i = cursor;
                    // If we're between characters, start from the char to the left
                    i--;
                    // Skip separators left of the cursor
                    while (i >= 0 && !IsWordChar(buffer[i])) i--;
                    // Then move to the start of the word
                    while (i >= 0 && IsWordChar(buffer[i])) i--;
                    cursor = Math.Max(0, i + 1);
                    Render();
                }
            }
            else if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow)
            {
                if (cursor < buffer.Length)
                {
                    int i = cursor;
                    // Skip separators to the right
                    while (i < buffer.Length && !IsWordChar(buffer[i])) i++;
                    // Then move to end of the word
                    while (i < buffer.Length && IsWordChar(buffer[i])) i++;
                    cursor = i;
                    Render();
                }
            }
            else if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    int start = cursor;
                    int i = cursor - 1;
                    // Skip separators left of cursor
                    while (i >= 0 && !IsWordChar(buffer[i])) i--;
                    // Then the word to the left
                    while (i >= 0 && IsWordChar(buffer[i])) i--;
                    int delFrom = Math.Max(0, i + 1);
                    int delLen = start - delFrom;
                    if (delLen > 0)
                    {
                        buffer.Remove(delFrom, delLen);
                        cursor = delFrom;
                        Render();
                    }
                }
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
            else if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Delete)
            {
                if (cursor < buffer.Length)
                {
                    int i = cursor;
                    // Skip separators to the right
                    while (i < buffer.Length && !IsWordChar(buffer[i])) i++;
                    // Then the word to the right
                    while (i < buffer.Length && IsWordChar(buffer[i])) i++;
                    int delLen = Math.Max(0, i - cursor);
                    if (delLen > 0)
                    {
                        buffer.Remove(cursor, delLen);
                        Render();
                    }
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

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
    }
}
