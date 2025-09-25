using System.Globalization;

namespace AppConfigCli.Editor.Commands;

internal sealed record Undo(int Start, int End) : Command
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
                return (true, new Undo(-1, -1), null);
            var (ok, s, e, err) = TryParseRange(args, "Usage: u|undo <n> [m] | all");
            return ok ? (true, new Undo(s, e), null) : (false, null, err);
        }
    };

    public override Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        var args = Start == End
            ? Start == -1
                ? ["all"]
                : [Start.ToString(CultureInfo.InvariantCulture)]
            : new[] { Start.ToString(CultureInfo.InvariantCulture), End.ToString(CultureInfo.InvariantCulture) };
        UndoFn(app, args);
        return Task.FromResult(new CommandResult());
    }


    internal void UndoFn(EditorApp app, string[] args)
    {
        if (args.Length == 0)
        {
            app.ConsoleEx.WriteLine("Usage: u|undo <n> [m]  (undos rows n..m)");
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            UndoAll(app);
        }
        else
        {
            UndoRange(app, args);
        }
    }

    internal bool UndoRange(EditorApp app, string[] args)
    {
        if (!int.TryParse(args[0], out int start)) { app.ConsoleEx.WriteLine("First argument must be an index or 'all'."); app.ConsoleEx.ReadLine(); return false; }
        int end = start;
        if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
        var actualIndices = app.MapVisibleRangeToItemIndices(start, end, out var error);
        if (actualIndices is null)
        {
            app.ConsoleEx.WriteLine(error);
            app.ConsoleEx.ReadLine();
            return false;
        }

        int removedNew = 0, restored = 0, untouched = 0;
        foreach (var idx in actualIndices.OrderByDescending(i => i))
        {
            if (idx < 0 || idx >= app.Items.Count) { continue; }
            var item = app.Items[idx];
            if (item.IsNew)
            {
                app.Items.RemoveAt(idx);
                removedNew++;
            }
            else if (item.State == ItemState.Deleted)
            {
                item.State = ItemState.Unchanged;
                restored++;
            }
            else if (item.State == ItemState.Modified)
            {
                item.Value = item.OriginalValue;
                item.State = ItemState.Unchanged;
                restored++;
            }
            else
            {
                untouched++;
            }
        }

        app.ConsoleEx.WriteLine($"Undo selection: removed {removedNew} new item(s), restored {restored} item(s), untouched {untouched}.");
        app.ConsoleEx.WriteLine("Press Enter to continue...");
        app.ConsoleEx.ReadLine();
        return true;
    }

    internal void UndoAll(EditorApp app)
    {
        int removedNew = 0, restored = 0, untouched = 0;
        // Iterate descending to safely remove new items
        for (int idx = app.Items.Count - 1; idx >= 0; idx--)
        {
            var item = app.Items[idx];
            if (item.IsNew)
            {
                app.Items.RemoveAt(idx);
                removedNew++;
            }
            else if (item.State == ItemState.Deleted)
            {
                item.State = ItemState.Unchanged;
                restored++;
            }
            else if (item.State == ItemState.Modified)
            {
                item.Value = item.OriginalValue;
                item.State = ItemState.Unchanged;
                restored++;
            }
            else
            {
                untouched++;
            }
        }

        app.ConsoleEx.WriteLine($"Undo all: removed {removedNew} new item(s), restored {restored} item(s), untouched {untouched}.");
        app.ConsoleEx.WriteLine("Press Enter to continue...");
        app.ConsoleEx.ReadLine();
    }
}
