namespace AppConfigCli;

internal sealed class ConsoleTheme
{
    public ConsoleColor Default { get; init; }
    public ConsoleColor Control { get; init; }
    public ConsoleColor Number { get; init; }
    public ConsoleColor Letters { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsDefaultPreset { get; init; } = false;

    public ConsoleTheme(ConsoleColor @default, ConsoleColor control, ConsoleColor number, ConsoleColor letters)
    {
        Default = @default;
        Control = control;
        Number = number;
        Letters = letters;
    }

    public static ConsoleTheme Load(string? nameOrNull = null, bool noColor = false)
    {
        if (noColor)
            return new ConsoleTheme(Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor) { Enabled = false };

        var def = Console.ForegroundColor;
        var control = ConsoleColor.DarkYellow;
        var number = ConsoleColor.Cyan;
        var letters = ConsoleColor.Yellow; // letters color for keys/values distinct from UI default

        // Preset by name via CLI or env
        var themeName = nameOrNull ?? Environment.GetEnvironmentVariable("APP_CONFIG_THEME");
        if (!string.IsNullOrWhiteSpace(themeName))
        {
            var preset = FromName(themeName!.Trim(), def);
            if (preset is not null) return preset;
        }

        var envNoColor = Environment.GetEnvironmentVariable("APP_CONFIG_NO_COLOR");
        if (!string.IsNullOrWhiteSpace(envNoColor) && (envNoColor.Equals("1") || envNoColor.Equals("true", StringComparison.OrdinalIgnoreCase)))
            return new ConsoleTheme(def, def, def, def) { Enabled = false };

        var envDefault = Environment.GetEnvironmentVariable("APP_CONFIG_COLOR_DEFAULT");
        var envControl = Environment.GetEnvironmentVariable("APP_CONFIG_COLOR_CONTROL");
        var envNumber = Environment.GetEnvironmentVariable("APP_CONFIG_COLOR_NUMBER");
        var envLetters = Environment.GetEnvironmentVariable("APP_CONFIG_COLOR_LETTERS");

        if (TryParseColor(envDefault, out var d)) def = d;
        if (TryParseColor(envControl, out var c)) control = c;
        if (TryParseColor(envNumber, out var n)) number = n;
        if (TryParseColor(envLetters, out var l)) letters = l;
        return new ConsoleTheme(def, control, number, letters);
    }

    public static ConsoleTheme? FromName(string name, ConsoleColor fallbackDefault)
    {
        switch (name.ToLowerInvariant())
        {
            case "default":
                // UI default = fallbackDefault, letters = Yellow, control = Red, numbers = Green
                return new ConsoleTheme(fallbackDefault, ConsoleColor.Red, ConsoleColor.Green, ConsoleColor.Yellow) { IsDefaultPreset = true };
            case "mono":
            case "monochrome":
                return new ConsoleTheme(fallbackDefault, fallbackDefault, fallbackDefault, fallbackDefault);
            case "no-color":
            case "none":
                return new ConsoleTheme(fallbackDefault, fallbackDefault, fallbackDefault, fallbackDefault) { Enabled = false };
            case "solarized":
                return new ConsoleTheme(ConsoleColor.Gray, ConsoleColor.DarkYellow, ConsoleColor.DarkCyan, ConsoleColor.Yellow);
            default:
                return null;
        }
    }

    private static bool TryParseColor(string? text, out ConsoleColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Enum.TryParse<ConsoleColor>(text.Trim(), ignoreCase: true, out color);
    }
}
