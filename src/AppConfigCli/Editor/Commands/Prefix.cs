using System.Text;

namespace AppConfigCli.Editor.Commands;

internal sealed record Prefix(string? Value, bool Prompt) : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "p", "prefix" },
        Summary = "p|prefix [value]",
        Usage = "Usage: p|prefix [value]  (no arg prompts)",
        Description = "Change prefix (no arg prompts)",
        Parser = args => args.Length == 0 ? (true, new Prefix(null, Prompt: true), null) : (true, new Prefix(string.Join(' ', args), Prompt: false), null)
    };
    public override async Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        string[] args = Prompt ? [] : [Value ?? string.Empty];
        string? newPrefix = null;
        if (args.Length == 0)
        {
            Console.WriteLine("Enter new prefix (empty for all keys):");
            var prefixes = await app.GetPrefixCandidatesAsync().ConfigureAwait(false);
            var typed = ReadLineWithAutocomplete(prefixes, app.Theme);
            if (typed is null) return new CommandResult(); // ESC cancels
            newPrefix = typed;
        }
        else
        {
            newPrefix = string.Join(' ', args).Trim();
        }

        app.Prefix = newPrefix; // can be empty string to mean 'all keys'
        await app.LoadAsync();
        return new CommandResult();
    }

    private static string? ReadLineWithAutocomplete(IReadOnlyCollection<string> candidates, ConsoleTheme theme)
    {
        var buffer = new StringBuilder();
        int startLeft, startTop;
        try { Console.Write("> "); startLeft = Console.CursorLeft; startTop = Console.CursorTop; }
        catch { startLeft = 0; startTop = 0; }

        int matchIndex = 0;
        string? currentSuggestion = null;
        int lastPrinted = 0;

        void UpdateSuggestion()
        {
            var typed = buffer.ToString();
            var matches = candidates.Where(s => s.StartsWith(typed, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
            {
                currentSuggestion = null; matchIndex = 0; return;
            }
            if (matchIndex >= matches.Count) matchIndex = 0;
            if (matchIndex < 0) matchIndex = matches.Count - 1;
            currentSuggestion = matches[matchIndex];
        }

        void Render()
        {
            var typed = buffer.ToString();
            string remainder = string.Empty;
            if (!string.IsNullOrEmpty(currentSuggestion) && currentSuggestion!.StartsWith(typed, StringComparison.Ordinal) && currentSuggestion.Length > typed.Length)
            {
                remainder = currentSuggestion.Substring(typed.Length);
            }
            try
            {
                Console.SetCursorPosition(startLeft, startTop);
            }
            catch { }
            Console.Write(typed);
            int printed = typed.Length;
            if (remainder.Length > 0 && theme.Enabled)
            {
                var prev = Console.ForegroundColor;
                var col = ConsoleColor.DarkGray;
                if (Console.ForegroundColor != col) Console.ForegroundColor = col;
                Console.Write(remainder);
                if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;
                printed += remainder.Length;
            }
            // Clear any trailing leftover from previous render
            if (printed < lastPrinted)
            {
                Console.Write(new string(' ', lastPrinted - printed));
            }
            // Place cursor back at end of typed input
            try { Console.SetCursorPosition(startLeft + typed.Length, startTop); } catch { }
            lastPrinted = printed;
        }

        UpdateSuggestion();
        Render();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Escape || ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C))
            {
                Console.WriteLine();
                return null; // cancel
            }
            if (key.Key == ConsoleKey.Tab)
            {
                if (!string.IsNullOrEmpty(currentSuggestion))
                {
                    buffer.Clear();
                    buffer.Append(currentSuggestion);
                    UpdateSuggestion();
                    Render();
                }
                continue;
            }
            if (key.Key == ConsoleKey.UpArrow)
            {
                matchIndex--; UpdateSuggestion(); Render(); continue;
            }
            if (key.Key == ConsoleKey.DownArrow)
            {
                matchIndex++; UpdateSuggestion(); Render(); continue;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) { buffer.Remove(buffer.Length - 1, 1); matchIndex = 0; UpdateSuggestion(); Render(); }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                matchIndex = 0;
                UpdateSuggestion();
                Render();
                continue;
            }
        }
    }
}
