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
                void AddPathDict(Dictionary<string, object> node, string[] segments, string value)
                {
                    if (segments.Length == 0)
                    {
                        node["__value"] = value;
                        return;
                    }
                    var head = segments[0];
                    if (!node.TryGetValue(head, out var child))
                    {
                        if (segments.Length == 1)
                        {
                            node[head] = value;
                            return;
                        }
                        bool nextIsIndex = int.TryParse(segments[1], out _);
                        if (nextIsIndex)
                        {
                            var list = new List<object?>();
                            node[head] = list;
                            AddPathList(list, segments[1..], value);
                        }
                        else
                        {
                            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                            node[head] = dict;
                            AddPathDict(dict, segments[1..], value);
                        }
                    }
                    else if (child is string s)
                    {
                        bool nextIsIndex = segments.Length > 1 && int.TryParse(segments[1], out _);
                        if (nextIsIndex)
                        {
                            var list = new List<object?>();
                            node[head] = list;
                            AddPathList(list, segments[1..], value);
                        }
                        else
                        {
                            var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                            node[head] = dict;
                            AddPathDict(dict, segments[1..], value);
                        }
                    }
                    else if (child is Dictionary<string, object> dict)
                    {
                        if (segments.Length == 1)
                        {
                            dict["__value"] = value;
                        }
                        else
                        {
                            AddPathDict(dict, segments[1..], value);
                        }
                    }
                    else if (child is List<object?> list)
                    {
                        AddPathList(list, segments[1..], value);
                    }
                }

                void AddPathList(List<object?> list, string[] segments, string value)
                {
                    if (segments.Length == 0) return; // nothing to set
                    var idxStr = segments[0];
                    if (!int.TryParse(idxStr, out int idx))
                    {
                        // Treat non-numeric as object under the array element
                        EnsureListSize(list, 1);
                        var head = 0; // fallback index
                        if (list[head] is not Dictionary<string, object> d)
                        {
                            d = new Dictionary<string, object>(StringComparer.Ordinal);
                            list[head] = d;
                        }
                        AddPathDict(d, segments, value);
                        return;
                    }
                    EnsureListSize(list, idx + 1);
                    var child = list[idx];
                    if (segments.Length == 1)
                    {
                        list[idx] = value;
                        return;
                    }
                    bool nextIsIndex = int.TryParse(segments[1], out _);
                    if (child is null)
                    {
                        if (nextIsIndex)
                        {
                            var inner = new List<object?>();
                            list[idx] = inner;
                            AddPathList(inner, segments[1..], value);
                        }
                        else
                        {
                            var d = new Dictionary<string, object>(StringComparer.Ordinal);
                            list[idx] = d;
                            AddPathDict(d, segments[1..], value);
                        }
                    }
                    else if (child is string s)
                    {
                        if (nextIsIndex)
                        {
                            var inner = new List<object?>();
                            list[idx] = inner;
                            AddPathList(inner, segments[1..], value);
                        }
                        else
                        {
                            var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                            list[idx] = d;
                            AddPathDict(d, segments[1..], value);
                        }
                    }
                    else if (child is Dictionary<string, object> d)
                    {
                        AddPathDict(d, segments[1..], value);
                    }
                    else if (child is List<object?> l)
                    {
                        AddPathList(l, segments[1..], value);
                    }
                }

                void EnsureListSize(List<object?> list, int size)
                {
                    while (list.Count < size) list.Add(null);
                }

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
