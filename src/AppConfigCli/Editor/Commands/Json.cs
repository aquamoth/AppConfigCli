namespace AppConfigCli;

internal partial record Command
{
    public sealed record Json(string Separator) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "json" },
            Summary = "json [sep]",
            Usage = "Usage: json [separator] (default ':')",
            Description = "Edit visible items as nested JSON split by <sep>",
            Parser = args =>
            {
                var sep = args.Length < 1 ? ":" : string.Join(' ', args);
                return (true, new Json(sep), null);
            }
        };

        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            string[] args = new[] { Separator };

            await Task.CompletedTask;
            if (app.Label is null)
            {
                Console.WriteLine("json requires an active label filter. Set one with l|label <value> first.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return new CommandResult();
            }

            var sep = string.Join(' ', args);
            if (string.IsNullOrEmpty(sep))
            {
                Console.WriteLine("Separator cannot be empty.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return new CommandResult();
            }

            string tmpDir = app.Filesystem.GetTempPath();
            string file = app.Filesystem.Combine(tmpDir, $"appconfig-json-{Guid.NewGuid():N}.json");

            try
            {
                var json = StructuredEditHelper.BuildJsonContent(app.GetVisibleItems(), sep);
                app.Filesystem.WriteAllText(file, json);

                // Launch editor
                try { app.ExternalEditor.Open(file); }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch editor: {ex.Message}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    return new CommandResult();
                }

                // Apply edits via StructuredEditHelper and return
                var editedJson = string.Join("\n", app.Filesystem.ReadAllLines(file));
                var (ok, err, cJ, uJ, dJ) = StructuredEditHelper.ApplyJsonEdits(editedJson, sep, app.Items, app.GetVisibleItems(), app.Prefix, app.Label);
                if (!ok)
                {
                    Console.WriteLine($"Invalid JSON: {err}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    return new CommandResult();
                }
                app.ConsolidateDuplicates();
                app.Items.Sort(EditorApp.CompareItems);
                Console.WriteLine($"JSON edit applied for label [{(app.Label?.Length == 0 ? "(none)" : app.Label) ?? "(any)"}]: {cJ} added, {uJ} updated, {dJ} deleted.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return new CommandResult();


            }
            finally
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }
    }
}
