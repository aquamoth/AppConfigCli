namespace AppConfigCli.Editor.Commands;

internal sealed record Quit() : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "q", "quit", "exit" },
        Summary = "q|quit",
        Usage = "Usage: q|quit",
        Description = "Quit the editor",
        Parser = args => (true, new Quit(), null)
    };
    public override async Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        var shouldExit = await TryQuitAsync(app).ConfigureAwait(false);
        return new CommandResult(ShouldExit: shouldExit);
    }
    internal async Task<bool> TryQuitAsync(EditorApp app)
    {
        if (app.HasPendingChanges(out var newCount, out var modCount, out var delCount))
        {
            app.ConsoleEx.WriteLine($"You have unsaved changes: +{newCount} new, *{modCount} modified, -{delCount} deleted.");
            app.ConsoleEx.WriteLine("Do you want to save before exiting?");
            app.ConsoleEx.WriteLine("  S) Save and quit");
            app.ConsoleEx.WriteLine("  Q) Quit without saving");
            app.ConsoleEx.WriteLine("  C) Cancel");
            while (true)
            {
                app.ConsoleEx.Write("> ");
                var (ctrlC, input) = app.ReadLineOrCtrlC_Engine();
                if (ctrlC) return false; // treat Ctrl+C here as cancel
                var choice = (input ?? string.Empty).Trim().ToLowerInvariant();
                if (choice.Length == 0) continue;
                var ch = choice[0];
                if (ch == 'c') return false; // cancel quit
                if (ch == 's') { await app.SaveAsync(pause: false); return true; }
                if (ch == 'q') { return true; }
                app.ConsoleEx.WriteLine("Please enter S, Q, or C.");
            }
        }
        return true;
    }
}
