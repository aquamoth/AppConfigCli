namespace AppConfigCli.Editor.Abstractions;

internal sealed class DefaultExternalEditor : IExternalEditor
{
    public void Open(string filePath)
    {
        var (exe, argsFmt) = GetDefaultEditor();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = string.Format(argsFmt, QuoteIfNeeded(filePath)),
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit();
    }

    private static (string exe, string argsFmt) GetDefaultEditor()
    {
        string? visual = Environment.GetEnvironmentVariable("VISUAL");
        string? editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(visual)) return (visual!, "{0}");
        if (!string.IsNullOrWhiteSpace(editor)) return (editor!, "{0}");
        if (OperatingSystem.IsWindows()) return ("notepad", "{0}");
        return ("nano", "{0}");
    }

    private static string QuoteIfNeeded(string path)
        => path.Contains(' ') ? $"\"{path}\"" : path;
}
