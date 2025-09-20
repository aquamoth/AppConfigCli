using System;
using System.Globalization;

namespace AppConfigCli;

internal static class CommandParser
{
    // Short, single-line summary used in the main UI footer
    public static string GetSummaryLine() =>
        "Commands: a|add, c|copy <n> [m], d|delete <n> [m], e|edit <n>, g|grep [regex], h|help, json <sep>, yaml <sep>, l|label [value], o|open, p|prefix [value], q|quit, r|reload, s|save, u|undo <n> [m]|all, w|whoami";

    // Full help block used by the Help screen
    public static string GetHelpText() => string.Join('\n', new[]
    {
        "Help - Commands",
        new string('-', 40),
        "a|add            Add a new key under the current prefix",
        "c|copy <n> [m]   Copy rows n..m to another label and switch",
        "d|delete <n> [m] Delete items n..m",
        "e|edit <n>       Edit value of item number n",
        "g|grep [regex]   Set key regex filter (no arg clears)",
        "h|help|?         Show this help",
        "json <sep>       Edit visible items as nested JSON split by <sep>",
        "yaml <sep>       Edit visible items as nested YAML split by <sep>",
        "o|open           Edit all visible items in external editor",
        "p|prefix [value] Change prefix (no arg prompts)",
        "l|label [value]  Change label filter (no arg clears; '-' = empty label)",
        "q|quit           Quit the editor",
        "r|reload         Reload from Azure and reconcile local changes",
        "s|save           Save all pending changes to Azure",
        "u|undo <n> [m]|all  Undo local changes for rows n..m, or 'all' to undo everything",
        "w|whoami         Show current identity and endpoint"
    });

    // Individual usage strings (returned in parse errors)
    private static string UsageEdit => "Usage: e|edit <n>";
    private static string UsageDelete => "Usage: d|delete <n> [m]";
    private static string UsageCopy => "Usage: c|copy <n> [m]";
    private static string UsageGrep => "Usage: g|grep [regex]";
    private static string UsageLabel => "Usage: l|label [value]  (no arg clears; '-' = empty)";
    private static string UsagePrefix => "Usage: p|prefix [value]  (no arg prompts)";
    private static string UsageUndo => "Usage: u|undo <n> [m] | all";
    private static string UsageJson => "Usage: json <separator>";
    private static string UsageYaml => "Usage: yaml <separator>";

    public static bool TryParse(string? input, out Command? command, out string? error)
    {
        command = null; error = null;
        if (string.IsNullOrWhiteSpace(input)) { error = ""; return false; }
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { error = ""; return false; }

        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "a":
            case "add":
                command = new Command.Add(); return true;

            case "e":
            case "edit":
                if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var eIdx))
                { error = UsageEdit; return false; }
                command = new Command.Edit(eIdx); return true;

            case "d":
            case "delete":
                if (!TryParseRange(parts, out var ds, out var de, out error)) { error = UsageDelete; return false; }
                command = new Command.Delete(ds, de); return true;

            case "c":
            case "copy":
                if (!TryParseRange(parts, out var cs, out var ce, out error)) { error = UsageCopy; return false; }
                command = new Command.Copy(cs, ce); return true;

            case "l":
            case "label":
                if (parts.Length == 1)
                { command = new Command.Label(null, Clear: true, Empty: false); return true; }
                if (parts[1] == "-")
                { command = new Command.Label("", Clear: false, Empty: true); return true; }
                command = new Command.Label(string.Join(' ', parts[1..]), Clear: false, Empty: false); return true;

            case "g":
            case "grep":
                if (parts.Length == 1) { command = new Command.Grep(null, Clear: true); return true; }
                command = new Command.Grep(string.Join(' ', parts[1..]), Clear: false); return true;

            case "s":
            case "save":
                command = new Command.Save(); return true;

            case "r":
            case "reload":
                command = new Command.Reload(); return true;

            case "q":
            case "quit":
            case "exit":
                command = new Command.Quit(); return true;

            case "h":
            case "?":
            case "help":
                command = new Command.Help(); return true;

            case "o":
            case "open":
                command = new Command.Open(); return true;

            case "p":
            case "prefix":
                if (parts.Length == 1) { command = new Command.Prefix(null, Prompt: true); return true; }
                command = new Command.Prefix(string.Join(' ', parts[1..]), Prompt: false); return true;

            case "u":
            case "undo":
                if (parts.Length == 2 && string.Equals(parts[1], "all", StringComparison.OrdinalIgnoreCase))
                { command = new Command.UndoAll(); return true; }
                if (!TryParseRange(parts, out var us, out var ue, out error)) { error = UsageUndo; return false; }
                command = new Command.UndoRange(us, ue); return true;

            case "json":
                if (parts.Length < 2) { error = UsageJson; return false; }
                command = new Command.Json(string.Join(' ', parts[1..])); return true;

            case "yaml":
                if (parts.Length < 2) { error = UsageYaml; return false; }
                command = new Command.Yaml(string.Join(' ', parts[1..])); return true;

            case "w":
            case "whoami":
                command = new Command.WhoAmI(); return true;
        }

        error = "Unknown command";
        return false;
    }

    private static bool TryParseRange(string[] parts, out int start, out int end, out string? error)
    {
        error = null; start = end = 0;
        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
        { error = ""; return false; }
        end = start;
        if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)) end = e;
        return true;
    }
}

