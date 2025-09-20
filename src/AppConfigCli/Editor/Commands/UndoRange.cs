using System.Globalization;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record UndoRange(int Start, int End) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "u", "undo" },
            Summary = "u|undo <n> [m]|all",
            Usage = "Usage: u|undo <n> [m] | all",
            Description = "Undo local changes for rows n..m, or 'all' to undo everything",
            Parser = args =>
            {
                if (args.Length == 1 && string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
                    return (true, new UndoAll(), null);
                var (ok, s, e, err) = TryParseRange(args, "Usage: u|undo <n> [m] | all");
                return ok ? (true, new UndoRange(s, e), null) : (false, null, err);
            }
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Start == End ? new[] { Start.ToString(CultureInfo.InvariantCulture) }
                                     : new[] { Start.ToString(CultureInfo.InvariantCulture), End.ToString(CultureInfo.InvariantCulture) };
            app.Undo(args);
            return Task.FromResult(new CommandResult());
        }
    }
}
