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
            await app.ChangePrefixAsync(args);
            return new CommandResult();
        }
    }
}
