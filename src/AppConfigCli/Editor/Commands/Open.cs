namespace AppConfigCli;

internal partial record Command
{
    public sealed record Open() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "o", "open" },
            Summary = "o|open",
            Usage = "Usage: o|open",
            Description = "Edit all visible items in external editor",
            Parser = args => (true, new Open(), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await Task.CompletedTask;
            if (app.Label is null)
            {
                Console.WriteLine("Open requires an active label filter. Set one with l|label <value> first.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return new CommandResult();
            }

            // Prepare temp file
            string tmpDir = app.Filesystem.GetTempPath();
            string file = app.Filesystem.Combine(tmpDir, $"appconfig-{Guid.NewGuid():N}.txt");

            try
            {
                var content = BulkEditHelper.BuildInitialFileContent(app.GetVisibleItems().Where(i => i.State != ItemState.Deleted), app.Prefix, app.Label);
                app.Filesystem.WriteAllText(file, content);

                // Launch editor
                try
                {
                    app.ExternalEditor.Open(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch editor: {ex.Message}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    return new CommandResult();
                }

                // Read back and reconcile
                var lines = app.Filesystem.ReadAllLines(file);
                var fileText = string.Join("\n", lines);
                var visibleUnderLabel = app.GetVisibleItems();
                var (created, updated, deleted) = BulkEditHelper.ApplyEdits(fileText, app.Items, visibleUnderLabel, app.Prefix, app.Label);
                app.ConsolidateDuplicates();
                app.Items.Sort(EditorApp.CompareItems);
                Console.WriteLine($"Bulk edit applied for label [{app.Label ?? "(none)"}]: {created} added, {updated} updated, {deleted} deleted.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return new CommandResult();
            }
            finally
            {
                app.Filesystem.Delete(file);
            }
        }
    }
}
