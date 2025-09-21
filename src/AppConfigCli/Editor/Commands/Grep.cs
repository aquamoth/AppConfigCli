using System.Text.RegularExpressions;

namespace AppConfigCli;

internal partial record Command
{
    public sealed record Grep(string? Pattern, bool Clear) : Command
    {
        public static CommandSpec Spec => new CommandSpec
        {
            Aliases = new[] { "/", "g", "grep" },
            Summary = "/|grep [regex]",
            Usage = "Usage: /|grep [regex]",
            Description = "Filter keys by regular expression (case insensitive). Leave empty to clear the filter.",
            Parser = args => args.Length == 0
                ? (true, new Grep(null, Clear: true), null)
                : (true, new Grep(string.Join(' ', args), Clear: false), null)
        };

        public override Task<CommandResult> ExecuteAsync(EditorApp app)
        {
            var args = Clear ? System.Array.Empty<string>() : new[] { Pattern ?? string.Empty };

            if (args.Length == 0)
            {
                app.KeyRegexPattern = null;
                app.KeyRegex = null;
            }
            else
            {
                var pattern = string.Join(' ', args);
                try
                {
                    app.KeyRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    app.KeyRegexPattern = pattern;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Invalid regex: {ex.Message}");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                }
            }

            return Task.FromResult(new CommandResult());
        }
    }
}
