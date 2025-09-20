using System.Threading.Tasks;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Save() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "s", "save" },
            Summary = "s|save",
            Usage = "Usage: s|save",
            Description = "Save all pending changes to Azure",
            Parser = args => (true, new Save(), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.SaveAsync(true); // pause: true
            return new CommandResult();
        }
    }

    public sealed record Reload() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "r", "reload" },
            Summary = "r|reload",
            Usage = "Usage: r|reload",
            Description = "Reload from Azure and reconcile local changes",
            Parser = args => (true, new Reload(), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.LoadAsync();
            return new CommandResult();
        }
    }

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
            await app.OpenInEditorAsync();
            return new CommandResult();
        }
    }

    public sealed record Json(string Separator) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "json" },
            Summary = "json <sep>",
            Usage = "Usage: json <separator>",
            Description = "Edit visible items as nested JSON split by <sep>",
            Parser = args => args.Length < 1 ? (false, null, "Usage: json <separator>") : (true, new Json(string.Join(' ', args)), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            await app.OpenJsonInEditorAsync(new[] { Separator });
            return new CommandResult();
        }
    }

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

    public sealed record Prefix(string? Value, bool Prompt) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "p", "prefix" },
            Summary = "p|prefix [value]",
            Usage = "Usage: p|prefix [value]  (no arg prompts)",
            Description = "Change prefix (no arg prompts)",
            Parser = args => args.Length == 0 ? (true, new Prefix(null, Prompt: true), null) : (true, new Prefix(string.Join(' ', args), Prompt: false), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Prompt ? System.Array.Empty<string>() : new[] { Value ?? string.Empty };
            await app.ChangePrefixAsync(args);
            return new CommandResult();
        }
    }

    public sealed record Help() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "h", "help", "?" },
            Summary = "h|help",
            Usage = "Usage: h|help",
            Description = "Show this help",
            Parser = args => (true, new Help(), null)
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            app.ShowHelp();
            return Task.FromResult(new CommandResult());
        }
    }

    public sealed record WhoAmI() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "w", "whoami" },
            Summary = "w|whoami",
            Usage = "Usage: w|whoami",
            Description = "Show current identity and endpoint",
            Parser = args => (true, new WhoAmI(), null)
        };
        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            return app.InvokeWhoAmIAsync().ContinueWith(_ => new CommandResult());
        }
    }

    public sealed record Quit() : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "q", "quit", "exit" },
            Summary = "q|quit",
            Usage = "Usage: q|quit",
            Description = "Quit the editor",
            Parser = args => (true, new Quit(), null)
        };
        public override async Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var shouldExit = await app.TryQuitAsync().ConfigureAwait(false);
            return new CommandResult(ShouldExit: shouldExit);
        }
    }
}
