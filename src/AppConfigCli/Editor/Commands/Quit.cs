namespace AppConfigCli;

internal partial record Command
{
    public sealed record Quit() : Command
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
                Console.WriteLine($"You have unsaved changes: +{newCount} new, *{modCount} modified, -{delCount} deleted.");
                Console.WriteLine("Do you want to save before exiting?");
                Console.WriteLine("  S) Save and quit");
                Console.WriteLine("  Q) Quit without saving");
                Console.WriteLine("  C) Cancel");
                while (true)
                {
                    Console.Write("> ");
                    var (ctrlC, input) = EditorApp.ReadLineOrCtrlC();
                    if (ctrlC) return false; // treat Ctrl+C here as cancel
                    var choice = (input ?? string.Empty).Trim().ToLowerInvariant();
                    if (choice.Length == 0) continue;
                    var ch = choice[0];
                    if (ch == 'c') return false; // cancel quit
                    if (ch == 's') { await app.SaveAsync(pause: false); return true; }
                    if (ch == 'q') { return true; }
                    Console.WriteLine("Please enter S, Q, or C.");
                }
            }
            return true;
        }
    }
}
