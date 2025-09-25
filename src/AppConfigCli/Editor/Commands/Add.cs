using System.Text;

namespace AppConfigCli.Editor.Commands;

internal sealed record Add(string? Key, bool Prompt) : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "a", "add" },
        Summary = "a|add [key]",
        Usage = "Usage: a|add [key]",
        Description = "Add a new key under the current prefix",
        Parser = args => args.Length == 0
            ? (true, new Add(null, Prompt: true), null)
            : (true, new Add(string.Join(' ', args), Prompt: false), null)
    };

    public override Task<CommandResult> ExecuteAsync(EditorApp app)
    {
        string? k = Key;
        if (Prompt)
        {
            app.ConsoleEx.WriteLine("Enter new key (relative to prefix):");
            k = ReadLineCancelable(app);
            if (k is null) return Task.FromResult(new CommandResult());
        }
        if (string.IsNullOrWhiteSpace(k)) return Task.FromResult(new CommandResult());
        k = k!.Trim();

        string? chosenLabel = app.Label;
        if (chosenLabel is not null)
        {
            app.ConsoleEx.WriteLine($"Adding new key under label: [{chosenLabel}]");
        }
        else
        {
            app.ConsoleEx.WriteLine("Enter label for new key (empty for none):");
            var lbl = ReadLineCancelable(app);
            if (lbl is null) return Task.FromResult(new CommandResult());
            chosenLabel = string.IsNullOrWhiteSpace(lbl) ? null : lbl!.Trim();
            app.ConsoleEx.WriteLine($"Using label: [{chosenLabel ?? "(none)"}]");
        }

        if (app.Items.Any(i => i.ShortKey.Equals(k, StringComparison.Ordinal)
                             && string.Equals(i.Label ?? string.Empty, chosenLabel ?? string.Empty, StringComparison.Ordinal)))
        {
            app.ConsoleEx.WriteLine("Key already exists for this label.");
            return Task.FromResult(new CommandResult());
        }

        app.ConsoleEx.WriteLine("Enter value:");
        app.ConsoleEx.Write("> ");
        var vRes = app.ReadLineWithPagingCancelable(
            onRepaint: () => { return (app.ConsoleEx.CursorLeft, app.ConsoleEx.CursorTop); },
            onPageUp: () => { },
            onPageDown: () => { },
            initial: string.Empty);
        var v = vRes.Cancelled ? null : vRes.Text;
        if (v is null) return Task.FromResult(new CommandResult());
        v ??= string.Empty;
        var basePrefix = app.Prefix ?? string.Empty;
        app.Items.Add(new Item
        {
            FullKey = basePrefix + k,
            ShortKey = k,
            Label = chosenLabel,
            OriginalValue = null,
            Value = v,
            State = ItemState.New
        });
        app.Items.Sort(EditorApp.CompareItems);

        if (string.IsNullOrEmpty(app.Prefix))
        {
            // Invalidate prefix cache so autocomplete sees the added new prefix
            app.TryAddPrefixFromKey(k);
        }

        return Task.FromResult(new CommandResult());
    }

    private static string? ReadLineCancelable(EditorApp app)
    {
        var buffer = new StringBuilder();
        int startLeft = 0, startTop = 0;
        try { app.ConsoleEx.Write("> "); startLeft = app.ConsoleEx.CursorLeft; startTop = app.ConsoleEx.CursorTop; } catch { }
        void Render()
        {
            try { app.ConsoleEx.SetCursorPosition(startLeft, startTop); } catch { }
            app.ConsoleEx.Write(buffer.ToString());
        }
        Render();
        while (true)
        {
            var key = app.ConsoleEx.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                app.ConsoleEx.WriteLine("");
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Escape || ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C))
            {
                app.ConsoleEx.WriteLine("");
                return null; // cancel
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    try { app.ConsoleEx.SetCursorPosition(Math.Max(0, startLeft + buffer.Length), startTop); } catch { }
                    app.ConsoleEx.Write(' ');
                    Render();
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Render();
            }
        }
    }
}
