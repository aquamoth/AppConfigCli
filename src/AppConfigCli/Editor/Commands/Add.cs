using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Add(string? Key, bool Prompt) : Command
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
                Console.WriteLine("Enter new key (relative to prefix):");
                k = ReadLineCancelable();
                if (k is null) return Task.FromResult(new CommandResult());
            }
            if (string.IsNullOrWhiteSpace(k)) return Task.FromResult(new CommandResult());
            k = k!.Trim();

            string? chosenLabel = app.Label;
            if (chosenLabel is not null)
            {
                Console.WriteLine($"Adding new key under label: [{chosenLabel}]");
            }
            else
            {
                Console.WriteLine("Enter label for new key (empty for none):");
                var lbl = ReadLineCancelable();
                if (lbl is null) return Task.FromResult(new CommandResult());
                chosenLabel = string.IsNullOrWhiteSpace(lbl) ? null : lbl!.Trim();
                Console.WriteLine($"Using label: [{chosenLabel ?? "(none)"}]");
            }

            if (app.Items.Any(i => i.ShortKey.Equals(k, StringComparison.Ordinal)
                                 && string.Equals(i.Label ?? string.Empty, chosenLabel ?? string.Empty, StringComparison.Ordinal)))
            {
                Console.WriteLine("Key already exists for this label.");
                return Task.FromResult(new CommandResult());
            }

            Console.WriteLine("Enter value:");
            Console.Write("> ");
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
    }

    private static string? ReadLineCancelable()
    {
        var buffer = new StringBuilder();
        int startLeft = 0, startTop = 0;
        try { Console.Write("> "); startLeft = Console.CursorLeft; startTop = Console.CursorTop; } catch { }
        void Render()
        {
            try { Console.SetCursorPosition(startLeft, startTop); } catch { }
            Console.Write(buffer.ToString());
        }
        Render();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Escape || ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C))
            {
                Console.WriteLine();
                return null; // cancel
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    try { Console.SetCursorPosition(Math.Max(0, startLeft + buffer.Length), startTop); } catch { }
                    Console.Write(' ');
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
