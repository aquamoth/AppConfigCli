namespace AppConfigCli.Editor.Commands;

internal sealed record WhoAmI() : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "whoami" },
        Summary = "whoami",
        Usage = "Usage: whoami",
        Description = "Show current identity and endpoint",
        Parser = args => (true, new WhoAmI(), null)
    };

    public override async Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        if (app.WhoAmI is not null)
        {
            await app.WhoAmI();
        }
        else
        {
            app.ConsoleEx.WriteLine("whoami not available in this mode.");
        }

        app.ConsoleEx.WriteLine("Press Enter to continue...");
        app.ConsoleEx.ReadLine();

        return new CommandResult();
    }
}
