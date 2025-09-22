using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Yaml(string Separator) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "y", "yaml" },
            Summary = "y|yaml [sep]",
            Usage = "Usage: yaml [separator] (default ':')",
            Description = "Edit visible items as nested YAML split by <sep> (default ':')",
            Parser = args =>
            {
                var sep = args.Length < 1 ? ":" : string.Join(' ', args);
                return (true, new Yaml(sep), null);
            }
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await OpenYamlInEditorAsync(app, new[] { Separator });
            return new CommandResult();
        }


        internal async Task OpenYamlInEditorAsync(EditorApp app, string[] args)
        {
            await Task.CompletedTask;
            if (app.Label is null)
            {
                Console.WriteLine("yaml requires an active label filter. Set one with l|label <value> first.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }

            var sep = string.Join(' ', args);
            if (string.IsNullOrEmpty(sep))
            {
                Console.WriteLine("Separator cannot be empty.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }

            string tmpDir = app.Filesystem.GetTempPath();
            string file = app.Filesystem.Combine(tmpDir, $"appconfig-yaml-{Guid.NewGuid():N}.yaml");

            try
            {
                // Build flats and map to nested tree via FlatKeyMapper
                var flats = app.GetVisibleItems()
                    .Where(i => i.State != ItemState.Deleted)
                    .ToDictionary(i => i.ShortKey, i => i.Value ?? string.Empty, StringComparer.Ordinal);
                var root = AppConfigCli.Core.FlatKeyMapper.BuildTree(flats, sep);
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(root);
                File.WriteAllText(file, yaml);

                // Launch editor
                try { app.ExternalEditor.Open(file); }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch editor: {ex.Message}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    return;
                }



                // Apply edits via StructuredEditHelper and return
                var editedYaml = string.Join("\n", app.Filesystem.ReadAllLines(file));
                var (ok2, err2, c2, u2, d2) = StructuredEditHelper.ApplyYamlEdits(editedYaml, sep, app.Items, app.GetVisibleItems(), app.Prefix, app.Label);
                if (!ok2)
                {
                    Console.WriteLine($"Invalid YAML: {err2}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    return;
                }
                app.ConsolidateDuplicates();
                app.Items.Sort(EditorApp.CompareItems);
                // No summary/pause on success per UX request
                return;
            }
            finally
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }
    }
}
