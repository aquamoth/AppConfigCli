using System.Globalization;
using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Edit(int Index) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "e", "edit" },
            Summary = "e|edit <n>",
            Usage = "Usage: e|edit <n>",
            Description = "Edit value of item number n",
            Parser = args =>
            {
                if (args.Length >= 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return (true, new Edit(i), null);
                return (false, null, "Usage: e|edit <n>");
            }
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            app.Edit(new[] { Index.ToString(CultureInfo.InvariantCulture) });
            return Task.FromResult(new CommandResult());
        }
    }
}
