using System.Globalization;

namespace AppConfigCli.Editor.Commands;

internal sealed record Copy(int Start, int End) : Command
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
        await CopyAsync(app, args);
        return new CommandResult();
    }

    internal async Task CopyAsync(EditorApp app, string[] args)
    {
        if (app.Label is null)
        {
            app.ConsoleEx.WriteLine("Copy requires an active label filter. Set one with l|label <value> first.");
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
            return;
        }

        if (args.Length == 0)
        {
            app.ConsoleEx.WriteLine("Usage: c|copy <n> [m]  (copies rows n..m)");
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
            return;
        }

        if (!int.TryParse(args[0], out int start)) { app.ConsoleEx.WriteLine("First argument must be an index."); app.ConsoleEx.ReadLine(); return; }
        int end = start;
        if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
        var actualIndices = app.MapVisibleRangeToItemIndices(start, end, out var error);
        if (actualIndices is null) { app.ConsoleEx.WriteLine(error); app.ConsoleEx.ReadLine(); return; }

        var selection = new List<(string ShortKey, string Value)>();
        foreach (var idx in actualIndices)
        {
            var it = app.Items[idx];
            if (it.State == ItemState.Deleted) continue;
            var val = it.Value ?? string.Empty;
            selection.Add((it.ShortKey, val));
        }
        if (selection.Count == 0)
        {
            app.ConsoleEx.WriteLine("Nothing to copy in the selected range."); app.ConsoleEx.ReadLine(); return;
        }

        app.ConsoleEx.WriteLine("Copy to label (empty for none):");
        app.ConsoleEx.Write("> ");
        var target = app.ConsoleEx.ReadLine();
        string? targetLabel = string.IsNullOrWhiteSpace(target) ? null : target!.Trim();

        // Switch to target label and load items for that label
        app.Label = targetLabel;
        await app.LoadAsync();

        int created = 0, updated = 0;
        foreach (var (shortKey, value) in selection)
        {
            // Only consider an existing item under the target label, never touch other labels
            var existing = app.Items.FirstOrDefault(x =>
                x.ShortKey.Equals(shortKey, StringComparison.Ordinal) &&
                x.Label == targetLabel);
            if (existing is null)
            {
                app.Items.Add(new Item
                {
                    FullKey = app.Prefix + shortKey,
                    ShortKey = shortKey,
                    Label = targetLabel,
                    OriginalValue = null,
                    Value = value,
                    State = ItemState.New
                });
                created++;
            }
            else
            {
                existing.Value = value;
                if (existing.OriginalValue == value)
                    existing.State = ItemState.Unchanged;
                else if (!existing.IsNew)
                    existing.State = ItemState.Modified;
                updated++;
            }
        }

        app.Items.Sort(EditorApp.CompareItems);
        // No summary/pause on success per UX request
    }
}
