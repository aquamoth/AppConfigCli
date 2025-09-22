namespace AppConfigCli.Editor.Commands;

internal sealed record Help() : Command
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
        Console.Clear();
        // Header: App name + version, author, project site, license
        Console.WriteLine(VersionInfo.GetVersionLine());

        string labelAuthor = "Author:";
        string labelProject = "Project website:";
        string labelLicense = "Licensed under Apache 2.0:";
        int labelWidth = new[] { labelAuthor, labelProject, labelLicense }.Max(s => s.Length);

        void WriteHeaderRow(string label, string value, bool isUrl)
        {
            var padded = label.PadRight(labelWidth);
            Console.Write(padded + " ");
            if (isUrl) WriteUrl(app, value); else Console.Write(value);
            Console.WriteLine();
        }

        // Author row: name + personal GitHub link
        var paddedAuthor = labelAuthor.PadRight(labelWidth);
        Console.Write(paddedAuthor + " ");
        Console.Write("Mattias Åslund ");
        WriteUrl(app, "https://github.com/aquamoth");
        Console.WriteLine();
        WriteHeaderRow(labelProject, "https://github.com/aquamoth/AppConfigCli", isUrl: true);
        WriteHeaderRow(labelLicense, "https://www.apache.org/licenses/LICENSE-2.0", isUrl: true);
        Console.WriteLine();

        // Helpers moved to class scope

        // Display group (sorted)
        Console.WriteLine("Display");
        var displayAliases = new[] { "prefix", "label", "grep", "reload", "quit", "help", "whoami" };
        var displaySpecs = displayAliases.Select(a => FindSpec(a)).Where(s => s is not null)!.Cast<Command.CommandSpec>()
            .OrderBy(s => LongAlias(s), StringComparer.OrdinalIgnoreCase);
        foreach (var s in displaySpecs)
            WriteCommandLine(s);
        Console.WriteLine();
        WriteWrappedRow("PageUp/PageDown key", "Change page when list is longer than screen");
        Console.WriteLine();

        // Inline editing group (numeric edit first)
        Console.WriteLine("Inline editing");
        // Numeric edit hint (no short alias; indent 5 spaces) shown before other items
        WriteWrappedRow("     <n>", "Edit value of item number n");
        var editAliases = new[] { "add", /* "edit" removed: numeric input edits */ "delete", "copy", "replace", "save", "undo" };
        var editSpecs = editAliases.Select(a => FindSpec(a)).Where(s => s is not null)!.Cast<Command.CommandSpec>()
            .OrderBy(s => LongAlias(s), StringComparer.OrdinalIgnoreCase);
        foreach (var s in editSpecs)
            WriteCommandLine(s);
        Console.WriteLine();
        WriteWrappedRow("Up/Down key", "Browse command history at the prompt");
        Console.WriteLine();

        // External editor group (sorted)
        Console.WriteLine("Edit in external editor");
        var externalAliases = new[] { "open", "json", "yaml" };
        var externalSpecs = externalAliases.Select(a => FindSpec(a)).Where(s => s is not null)!.Cast<Command.CommandSpec>()
            .OrderBy(s => LongAlias(s), StringComparer.OrdinalIgnoreCase);
        foreach (var s in externalSpecs)
            WriteCommandLine(s);
        Console.WriteLine();

        Console.WriteLine("Press Enter to return to the list...");
        Console.ReadLine();
        return Task.FromResult(new CommandResult());
    }

    private static void WriteUrl(EditorApp app, string url)
    {
        var prev = Console.ForegroundColor;
        if (app.Theme.Enabled && app.Theme.IsDefaultPreset)
            Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write(url);
        if (Console.ForegroundColor != prev) Console.ForegroundColor = prev;
    }

    private static bool HasShortAlias(Command.CommandSpec s)
        => s.Aliases.Any(a => a.Length == 1);

    private static string Row(string left, string right)
    {
        const int col = 26; // left column width
        var l = left.Length >= col ? left : left.PadRight(col);
        return l + right;
    }

    private static Command.CommandSpec? FindSpec(string alias)
        => Command.AllSpecs.FirstOrDefault(s => Array.Exists(s.Aliases, a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)));

    private static string LongAlias(Command.CommandSpec s)
        => s.Aliases.FirstOrDefault(a => a.Length > 1) ?? s.Aliases.First();

    private static string FormatSummary(Command.CommandSpec s)
        => (s.Summary ?? string.Empty).Replace("|", ", ");

    private static void WriteCommandLine(Command.CommandSpec s)
    {
        var summary = FormatSummary(s);
        var indent = HasShortAlias(s) ? "  " : "     ";
        WriteWrappedRow(indent + summary, s.Description);
    }

    private static void WriteWrappedRow(string left, string description)
    {
        const int maxWidth = 80; // fixed per requirement
        const int col = 26; // left column width; description starts at col+1 (1-based pos 27)
        int descWidth = Math.Max(1, maxWidth - col);
        var chunks = WrapText(description ?? string.Empty, descWidth);

        if (chunks.Count == 0)
        {
            Console.WriteLine(Row(left, string.Empty));
            return;
        }

        // First line with left label
        Console.WriteLine(Row(left, chunks[0]));
        // Continuations aligned with description start
        string contPad = new string(' ', col);
        for (int i = 1; i < chunks.Count; i++)
        {
            Console.WriteLine(contPad + chunks[i]);
        }
    }

    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        int i = 0;
        while (i < text.Length)
        {
            int remain = text.Length - i;
            int take = Math.Min(width, remain);
            // try to break on last whitespace within the width
            int breakPos = -1;
            for (int j = 0; j < take; j++)
            {
                if (char.IsWhiteSpace(text[i + j])) breakPos = j;
            }
            if (take == remain)
            {
                lines.Add(text.Substring(i, take).TrimEnd());
                break;
            }
            if (breakPos <= 0)
            {
                // no whitespace found; hard-break
                lines.Add(text.Substring(i, take));
                i += take;
            }
            else
            {
                lines.Add(text.Substring(i, breakPos));
                // skip whitespace after break
                i += breakPos + 1;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            }
        }
        return lines;
    }
}
