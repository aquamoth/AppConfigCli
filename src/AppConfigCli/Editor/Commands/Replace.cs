using System.Text.RegularExpressions;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Replace() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "replace" },
            Summary = "replace",
            Usage = "Usage: replace",
            Description = "Search and replace over all VISIBLE values using a regular expression.",
            Parser = args => (true, new Replace(), null)
        };

        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            // 1) Prompt for search regex (ESC/Ctrl+C cancels)
            app.ConsoleEx.WriteLine("Enter search regex (applies to VALUES only):");
            app.ConsoleEx.Write("> ");
            var patResult = app.ReadLineWithPagingCancelable(
                onRepaint: () =>
                {
                    app.Repaint();
                    app.ConsoleEx.WriteLine("Enter search regex (applies to VALUES only):");
                    app.ConsoleEx.Write("> ");
                    return (app.ConsoleEx.CursorLeft, app.ConsoleEx.CursorTop);
                },
                onPageUp: () => app.PageUpCommand(),
                onPageDown: () => app.PageDownCommand(),
                initial: null
            );
            var pattern = patResult.Cancelled ? null : patResult.Text;
            if (pattern is null) return Task.FromResult(new CommandResult());
            pattern = pattern.Trim();
            if (pattern.Length == 0) return Task.FromResult(new CommandResult());

            Regex rx;
            try
            {
                // Default to case-sensitive; inline modifiers like (?i) are supported by .NET regex
                rx = new Regex(pattern, RegexOptions.Compiled);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid regex: {ex.Message}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return Task.FromResult(new CommandResult());
            }

            // Optional preview: highlight matches in the Value column
            app.ValueHighlightRegex = rx;
            app.Repaint();

            // 2) Prompt for replacement text, allow paging during input
            app.ConsoleEx.WriteLine("Enter replacement text (supports $1, $2 for capture groups):");
            app.ConsoleEx.Write("> ");
            var replResult = app.ReadLineWithPagingCancelable(
                onRepaint: () =>
                {
                    app.Repaint();
                    app.ConsoleEx.WriteLine("Enter replacement text (supports $1, $2 for capture groups):");
                    app.ConsoleEx.Write("> ");
                    return (app.ConsoleEx.CursorLeft, app.ConsoleEx.CursorTop);
                },
                onPageUp: () => app.PageUpCommand(),
                onPageDown: () => app.PageDownCommand(),
                initial: null
            );
            string? replacement = replResult.Cancelled ? null : replResult.Text;
            // Clear preview highlight regardless of outcome
            app.ValueHighlightRegex = null;
            if (replacement is null) return Task.FromResult(new CommandResult());

            // 3) Apply over visible, non-deleted items' VALUEs
            var (itemsAffected, totalMatches) = ApplyReplace(app, rx, replacement);

            Console.WriteLine(totalMatches == 0
                ? "No matches found in visible values."
                : $"Replace complete: {totalMatches} match(es) across {itemsAffected} item(s).");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return Task.FromResult(new CommandResult());
        }

        // Internal for tests via InternalsVisibleTo
        internal static (int ItemsAffected, int TotalMatches) ApplyReplace(EditorApp app, Regex rx, string replacement)
        {
            var visible = app.GetVisibleItems();
            int itemsAffected = 0;
            int totalMatches = 0;
            foreach (var it in visible)
            {
                if (it.State == ItemState.Deleted) continue;
                var original = it.Value ?? string.Empty;
                var matches = rx.Matches(original);
                if (matches.Count == 0) continue;

                var updated = rx.Replace(original, replacement);
                if (!string.Equals(updated, original, StringComparison.Ordinal))
                {
                    it.Value = updated;
                    if (!it.IsNew)
                    {
                        it.State = string.Equals(it.OriginalValue ?? string.Empty, updated, StringComparison.Ordinal)
                            ? ItemState.Unchanged
                            : ItemState.Modified;
                    }
                    itemsAffected++;
                    totalMatches += matches.Count;
                }
            }
            return (itemsAffected, totalMatches);
        }
    }
}
