using System;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace AppConfigCli;

// Typed commands parsed from CLI input; each command executes itself on the EditorApp
internal sealed record CommandResult(bool ShouldExit = false);

internal abstract partial record Command
{
    public abstract Task<CommandResult> ExecuteAsync(EditorApp app);

    // Shared spec model for parser + help
    internal sealed class CommandSpec
    {
        public required string[] Aliases { get; init; }
        public required string Summary { get; init; }
        public required string Usage { get; init; }
        public required string Description { get; init; }
        public required Func<string[], (bool Ok, Command? Command, string? Error)> Parser { get; init; }
        public bool Matches(string token) => Array.Exists(Aliases, a => string.Equals(a, token, StringComparison.OrdinalIgnoreCase));
    }

    // Helper shared by range-based commands
    internal static (bool Ok, int Start, int End, string? Error) TryParseRange(string[] args, string usage)
    {
        if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
            return (false, 0, 0, usage);
        var end = start;
        if (args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)) end = e;
        return (true, start, end, null);
    }

    // Centralized list of all command specs (content lives in each command file)
    public static IReadOnlyList<CommandSpec> AllSpecs =>
    [
        Add.Spec,
        Edit.Spec,
        Delete.Spec,
        Copy.Spec,
        Label.Spec,
        Grep.Spec,
        Save.Spec,
        Reload.Spec,
        Quit.Spec,
        Help.Spec,
        Open.Spec,
        Prefix.Spec,
        Undo.Spec,
        Json.Spec,
        Yaml.Spec,
        WhoAmI.Spec,
    ];
}

