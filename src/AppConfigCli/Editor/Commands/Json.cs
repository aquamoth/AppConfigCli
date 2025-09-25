namespace AppConfigCli.Editor.Commands;

internal sealed record Json(string Separator) : Command
{
    public static CommandSpec Spec => new CommandSpec
    {
        Aliases = new[] { "j", "json" },
        Summary = "j|json [sep]",
        Usage = "Usage: json [separator] (default ':')",
        Description = "Edit visible items as nested JSON split by <sep> (default ':')",
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
            app.ConsoleEx.WriteLine("json requires an active label filter. Set one with l|label <value> first.");
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
            return new CommandResult();
        }

        var sep = string.Join(' ', args);
        if (string.IsNullOrEmpty(sep))
        {
            app.ConsoleEx.WriteLine("Separator cannot be empty.");
            app.ConsoleEx.WriteLine("Press Enter to continue...");
            app.ConsoleEx.ReadLine();
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
                app.ConsoleEx.WriteLine($"Failed to launch editor: {ex.Message}");
                app.ConsoleEx.WriteLine("Press Enter to continue...");
                app.ConsoleEx.ReadLine();
                return new CommandResult();
            }

            // Apply edits via StructuredEditHelper and return
            var editedJson = string.Join("\n", app.Filesystem.ReadAllLines(file));
            var (ok, err, cJ, uJ, dJ) = StructuredEditHelper.ApplyJsonEdits(editedJson, sep, app.Items, app.GetVisibleItems(), app.Prefix, app.Label);
            if (!ok)
            {
                app.ConsoleEx.WriteLine($"Invalid JSON: {err}");
                app.ConsoleEx.WriteLine("Press Enter to continue...");
                app.ConsoleEx.ReadLine();
                return new CommandResult();
            }
            app.ConsolidateDuplicates();
            app.Items.Sort(EditorApp.CompareItems);
            // No summary/pause on success per UX request
            return new CommandResult();


        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }
}
