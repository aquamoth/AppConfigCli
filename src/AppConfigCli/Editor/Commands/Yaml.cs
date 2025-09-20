namespace AppConfigCli;

internal partial record Command
{
    public sealed record Yaml(string Separator) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "yaml" },
            Summary = "yaml <sep>",
            Usage = "Usage: yaml <separator>",
            Description = "Edit visible items as nested YAML split by <sep>",
            Parser = args => args.Length < 1 ? (false, null, "Usage: yaml <separator>") : (true, new Yaml(string.Join(' ', args)), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.OpenYamlInEditorAsync(new[] { Separator });
            return new CommandResult();
        }
    }
}
