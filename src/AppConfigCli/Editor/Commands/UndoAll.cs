using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record UndoAll() : Command
    {
        // No separate Spec; covered by UndoRange.Spec Parser via 'all'
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            app.UndoAll();
            return Task.FromResult(new CommandResult());
        }
    }
}
