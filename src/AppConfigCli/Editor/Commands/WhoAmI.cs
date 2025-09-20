namespace AppConfigCli;

internal partial record Command
{
    public sealed record WhoAmI() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "w", "whoami" },
            Summary = "w|whoami",
            Usage = "Usage: w|whoami",
            Description = "Show current identity and endpoint",
            Parser = args => (true, new WhoAmI(), null)
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            return app.InvokeWhoAmIAsync().ContinueWith(_ => new CommandResult());
        }
    }
}
