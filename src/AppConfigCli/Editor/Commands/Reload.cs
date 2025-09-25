namespace AppConfigCli.Editor.Commands;

internal sealed record Reload() : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "r", "reload" },
        Summary = "r|reload",
        Usage = "Usage: r|reload",
        Description = "Reload from Azure and reconcile local changes",
        Parser = args => (true, new Reload(), null)
    };
    public override async Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        //We invalidate the prefix cache to ensure we dont have a stale prefix cache
        app.InvalidatePrefixCache();

        await app.LoadAsync();
        return new CommandResult();
    }
}
