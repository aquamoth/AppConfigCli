using System;
using System.Linq;
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
                Console.Write("> ");
                k = Console.ReadLine();
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
                Console.Write("> ");
                var lbl = Console.ReadLine();
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
            var v = Console.ReadLine() ?? string.Empty;
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
            return Task.FromResult(new CommandResult());
        }
    }
}
