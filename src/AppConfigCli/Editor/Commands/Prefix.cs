namespace AppConfigCli;

internal partial record Command
{
    public sealed record Prefix(string? Value, bool Prompt) : Command
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
            var args = Prompt ? System.Array.Empty<string>() : new[] { Value ?? string.Empty };
            string? newPrefix = null;
            if (args.Length == 0)
            {
                Console.WriteLine("Enter new prefix (empty for all keys):");
                Console.Write("> ");
                var input = Console.ReadLine();
                newPrefix = input is null ? string.Empty : input.Trim();
            }
            else
            {
                newPrefix = string.Join(' ', args).Trim();
            }

            if (app.HasPendingChanges(out var newCount, out var modCount, out var delCount))
            {
                Console.WriteLine($"You have unsaved changes: +{newCount} new, *{modCount} modified, -{delCount} deleted.");
                Console.WriteLine("Change prefix now?");
                Console.WriteLine("  S) Save and change");
                Console.WriteLine("  Q) Change without saving (discard)");
                Console.WriteLine("  C) Cancel");
                while (true)
                {
                    Console.Write("> ");
                    var choice = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                    if (choice.Length == 0) continue;
                    var ch = choice[0];
                    if (ch == 'c') return new CommandResult();
                    if (ch == 's') { await app.SaveAsync(pause: false); break; }
                    if (ch == 'q') { /* discard */ break; }
                    Console.WriteLine("Please enter S, Q, or C.");
                }
            }

            app.Prefix = newPrefix; // can be empty string to mean 'all keys'
            await app.LoadAsync();
            return new CommandResult();
        }
    }
}
