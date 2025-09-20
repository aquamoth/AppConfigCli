using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Label(string? Value, bool Clear, bool Empty) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "l", "label" },
            Summary = "l|label [value]",
            Usage = "Usage: l|label [value]  (no arg clears; '-' = empty)",
            Description = "Change label filter (no arg clears; '-' = empty label)",
            Parser = args =>
            {
                if (args.Length == 0) return (true, new Label(null, Clear: true, Empty: false), null);
                if (args[0] == "-") return (true, new Label("", Clear: false, Empty: true), null);
                return (true, new Label(string.Join(' ', args), Clear: false, Empty: false), null);
            }
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            string[] args;
            if (Clear) args = System.Array.Empty<string>();
            else if (Empty) args = new[] { "-" };
            else args = new[] { Value ?? string.Empty };
            await app.ChangeLabelAsync(args);
            return new CommandResult();
        }
    }

    public sealed record Grep(string? Pattern, bool Clear) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "g", "grep" },
            Summary = "g|grep [regex]",
            Usage = "Usage: g|grep [regex]",
            Description = "Set key regex filter (no arg clears)",
            Parser = args => args.Length == 0
                ? (true, new Grep(null, Clear: true), null)
                : (true, new Grep(string.Join(' ', args), Clear: false), null)
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Clear ? System.Array.Empty<string>() : new[] { Pattern ?? string.Empty };
            app.SetKeyRegex(args);
            return Task.FromResult(new CommandResult());
        }
    }
}
