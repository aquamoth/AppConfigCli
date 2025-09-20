namespace AppConfigCli;

internal partial record Command
{
    public sealed record Open() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "o", "open" },
            Summary = "o|open",
            Usage = "Usage: o|open",
            Description = "Edit all visible items in external editor",
            Parser = args => (true, new Open(), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.OpenInEditorAsync();
            return new CommandResult();
        }
    }
}
