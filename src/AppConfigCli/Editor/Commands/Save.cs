namespace AppConfigCli.Editor.Commands;

internal sealed record Save() : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "s", "save" },
        Summary = "s|save",
        Usage = "Usage: s|save",
        Description = "Save all pending changes to Azure",
        Parser = args => (true, new Save(), null)
    };
    public override async Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        await app.SaveAsync(true); // pause: true
        return new CommandResult();
    }
}
