using System;
using System.Collections.Generic;

namespace AppConfigCli;

internal interface IFileSystem
{
    string GetTempPath();
    string Combine(params string[] parts);
    void WriteAllText(string path, string contents);
    string[] ReadAllLines(string path);
    bool Exists(string path);
    void Delete(string path);
}

internal sealed class DefaultFileSystem : IFileSystem
{
    public string GetTempPath() => System.IO.Path.GetTempPath();
    public string Combine(params string[] parts) => System.IO.Path.Combine(parts);
    public void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);
    public string[] ReadAllLines(string path) => System.IO.File.ReadAllLines(path);
    public bool Exists(string path) => System.IO.File.Exists(path);
    public void Delete(string path) { try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { } }
}

internal interface IExternalEditor
{
    void Open(string filePath);
}

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

