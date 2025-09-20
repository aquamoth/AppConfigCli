namespace AppConfigCli;

internal partial record Command
{
    public sealed record Json(string Separator) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "json" },
            Summary = "json <sep>",
            Usage = "Usage: json <separator>",
            Description = "Edit visible items as nested JSON split by <sep>",
            Parser = args => args.Length < 1 ? (false, null, "Usage: json <separator>") : (true, new Json(string.Join(' ', args)), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.OpenJsonInEditorAsync(new[] { Separator });
            return new CommandResult();
        }
    }
}
