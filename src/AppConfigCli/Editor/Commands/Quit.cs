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
            var shouldExit = await app.TryQuitAsync().ConfigureAwait(false);
            return new CommandResult(ShouldExit: shouldExit);
        }
    }
}
