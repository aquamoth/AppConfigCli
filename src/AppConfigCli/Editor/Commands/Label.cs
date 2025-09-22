namespace AppConfigCli.Editor.Commands;

internal sealed record Label(string? Value, bool Clear, bool Empty) : Command
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

        if (args.Length == 0)
        {
            // No argument clears the filter (any label)
            app.Label = null;
            await app.LoadAsync();
            return new CommandResult();
        }

        var newLabelArg = string.Join(' ', args).Trim();
        if (newLabelArg == "-")
        {
            // Single dash selects the explicitly empty label
            app.Label = string.Empty;
        }
        else
        {
            // Any other value is a literal label
            app.Label = newLabelArg;
        }
        await app.LoadAsync();

        return new CommandResult();
    }
}
