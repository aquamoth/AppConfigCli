using System;
using System.Globalization;
using System.Linq;

namespace AppConfigCli;

internal static class CommandParser
{
    private static readonly System.Collections.Generic.IReadOnlyList<Command.CommandSpec> Specs = Command.AllSpecs;

    // Short, single-line summary used in the main UI footer (auto-generated)
    public static string GetSummaryLine() =>
        "Commands: " + string.Join(", ", Specs.Select(s => s.Summary));

    // Full help block used by the Help screen (auto-generated)
    public static string GetHelpText()
    {
        var lines = new List<string>
        {
            "Help - Commands",
            new string('-', 40)
        };
        foreach (var s in Specs)
        {
            var left = s.Summary.Length >= 18 ? s.Summary : s.Summary.PadRight(18);
            lines.Add(left + s.Description);
        }
        return string.Join('\n', lines);
    }

    public static bool TryParse(string? input, out Command? command, out string? error)
    {
        command = null; error = null;
        if (string.IsNullOrWhiteSpace(input)) { error = ""; return false; }
        var trimmed = input.Trim();
        // Numeric command -> Edit that index
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericIndex))
        {
            command = new Command.Edit(numericIndex);
            return true;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { error = ""; return false; }

        var cmdToken = parts[0];
        var spec = Specs.FirstOrDefault(s => s.Matches(cmdToken));
        if (spec is null) { error = "Unknown command"; return false; }
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
        var (ok, cmd, err) = spec.Parser(args);
        if (!ok)
        {
            error = err ?? spec.Usage;
            command = null;
            return false;
        }
        command = cmd;
        return true;
    }

    // Range parsing now lives in Command.TryParseRange via individual command Specs
}
