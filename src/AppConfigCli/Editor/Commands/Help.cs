namespace AppConfigCli;

internal partial record Command
{
    public sealed record Help() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "h", "help", "?" },
            Summary = "h|help",
            Usage = "Usage: h|help",
            Description = "Show this help",
            Parser = args => (true, new Help(), null)
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            Console.WriteLine();
            Console.WriteLine(CommandParser.GetHelpText());
            Console.WriteLine();
            Console.WriteLine("Press Enter to return to the list...");
            Console.ReadLine();
            return Task.FromResult(new CommandResult());
        }
    }
}
