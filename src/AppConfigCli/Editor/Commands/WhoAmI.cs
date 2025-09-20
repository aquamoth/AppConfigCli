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

        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            if (app.WhoAmI is not null)
            {
                await app.WhoAmI();
            }
            else
            {
                Console.WriteLine("whoami not available in this mode.");
            }

            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();

            return new CommandResult();
        }
    }
}
