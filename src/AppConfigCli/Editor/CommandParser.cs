using System;
using System.Globalization;

namespace AppConfigCli;

internal static class CommandParser
{
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
                { error = "Usage: e|edit <n>"; return false; }
                command = new Command.Edit(eIdx); return true;

            case "d":
            case "delete":
                if (!TryParseRange(parts, out var ds, out var de, out error)) return false;
                command = new Command.Delete(ds, de); return true;

            case "c":
            case "copy":
                if (!TryParseRange(parts, out var cs, out var ce, out error)) return false;
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
                if (!TryParseRange(parts, out var us, out var ue, out error)) return false;
                command = new Command.UndoRange(us, ue); return true;

            case "json":
                if (parts.Length < 2) { error = "Usage: json <separator>"; return false; }
                command = new Command.Json(string.Join(' ', parts[1..])); return true;

            case "yaml":
                if (parts.Length < 2) { error = "Usage: yaml <separator>"; return false; }
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
        { error = "First argument must be an index."; return false; }
        end = start;
        if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)) end = e;
        return true;
    }
}

