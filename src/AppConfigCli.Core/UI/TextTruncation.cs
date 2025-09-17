namespace AppConfigCli.Core.UI;

public static class TextTruncation
{
    /// <summary>
    /// Truncates text to a fixed width using a single-character ellipsis '…' when needed.
    /// If width <= 1, returns zero or one ellipsis accordingly.
    /// </summary>
    public static string TruncateFixed(string s, int width)
    {
        if (width <= 0) return string.Empty;
        if (s.Length <= width) return s;
        if (width == 1) return "…";
        return s[..(width - 1)] + "…";
    }

    /// <summary>
    /// Truncates to width and right-pads with spaces to exactly width characters.
    /// </summary>
    public static string PadColumn(string text, int width)
    {
        var t = TruncateFixed(text, width);
        return t.Length < width ? t.PadRight(width) : t;
    }
}

