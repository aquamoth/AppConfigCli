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

            app.Prefix = newPrefix; // can be empty string to mean 'all keys'
            await app.LoadAsync();
            return new CommandResult();
        }
    }
}
