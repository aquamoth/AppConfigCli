using System.Globalization;
using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Delete(int Start, int End) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "d", "delete" },
            Summary = "d|delete <n> [m]",
            Usage = "Usage: d|delete <n> [m]",
            Description = "Delete items n..m",
            Parser = args =>
            {
                var (ok, s, e, err) = TryParseRange(args, "Usage: d|delete <n> [m]");
                return ok ? (true, new Delete(s, e), null) : (false, null, err);
            }
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Start == End ? new[] { Start.ToString(CultureInfo.InvariantCulture) }
                                     : new[] { Start.ToString(CultureInfo.InvariantCulture), End.ToString(CultureInfo.InvariantCulture) };
            app.Delete(args);
            return Task.FromResult(new CommandResult());
        }
    }

    public sealed record Copy(int Start, int End) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "c", "copy" },
            Summary = "c|copy <n> [m]",
            Usage = "Usage: c|copy <n> [m]",
            Description = "Copy rows n..m to another label and switch",
            Parser = args =>
            {
                var (ok, s, e, err) = TryParseRange(args, "Usage: c|copy <n> [m]");
                return ok ? (true, new Copy(s, e), null) : (false, null, err);
            }
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Start == End ? new[] { Start.ToString(CultureInfo.InvariantCulture) }
                                     : new[] { Start.ToString(CultureInfo.InvariantCulture), End.ToString(CultureInfo.InvariantCulture) };
            await app.CopyAsync(args);
            return new CommandResult();
        }
    }
}
