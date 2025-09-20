using System.Globalization;

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
            var args = Start == End
                ? [Start.ToString(CultureInfo.InvariantCulture)]
                : new[] { Start.ToString(CultureInfo.InvariantCulture), End.ToString(CultureInfo.InvariantCulture) };

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: d|delete <n> [m]  (deletes rows n..m)");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return Task.FromResult(new CommandResult());
            }

            if (!int.TryParse(args[0], out int start))
            {
                Console.WriteLine("First argument must be an index.");
                Console.ReadLine();
                return Task.FromResult(new CommandResult());
            }

            int end = start;
            if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
            var actualIndices = app.MapVisibleRangeToItemIndices(start, end, out var error);
            if (actualIndices is null)
            {
                Console.WriteLine(error);
                Console.ReadLine();
                return Task.FromResult(new CommandResult());
            }

            int removedNew = 0, markedExisting = 0;
            foreach (var idx in actualIndices.OrderByDescending(i => i))
            {
                var item = app.Items[idx];
                if (item.IsNew)
                {
                    app.Items.RemoveAt(idx);
                    removedNew++;
                }
                else
                {
                    item.State = ItemState.Deleted;
                    markedExisting++;
                }
            }

            Console.WriteLine($"Deleted selection: removed {removedNew} new item(s), marked {markedExisting} existing item(s) for deletion.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return Task.FromResult(new CommandResult());
        }
    }
}
