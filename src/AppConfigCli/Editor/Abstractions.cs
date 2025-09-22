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

// Console abstraction for safer rendering
internal interface IConsoleEx
{
    int WindowWidth { get; }
    int WindowHeight { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    ConsoleColor ForegroundColor { get; set; }
    void SetCursorPosition(int left, int top);
    void Clear();
    void Write(string text);
    void Write(char ch);
    void WriteLine(string text);
}

internal sealed class DefaultConsoleEx : IConsoleEx
{
    public int WindowWidth { get { try { return Console.WindowWidth; } catch { return 80; } } }
    public int WindowHeight { get { try { return Console.WindowHeight; } catch { return 40; } } }
    public int CursorLeft { get { try { return Console.CursorLeft; } catch { return 0; } } }
    public int CursorTop { get { try { return Console.CursorTop; } catch { return 0; } } }
    public ConsoleColor ForegroundColor
    {
        get { try { return Console.ForegroundColor; } catch { return ConsoleColor.Gray; } }
        set { try { Console.ForegroundColor = value; } catch { } }
    }
    public void SetCursorPosition(int left, int top) { try { Console.SetCursorPosition(left, top); } catch { } }
    public void Clear() { try { Console.Clear(); } catch { } }
    public void Write(string text) { try { Console.Write(text); } catch { } }
    public void Write(char ch) { try { Console.Write(ch); } catch { } }
    public void WriteLine(string text)
    {
        try
        {
            Console.Write(text);
            //By not writing the line break when cursor already overflows, we avoid extra empty lines when run inside the old conhost (cmd.exe) on Windows.
            if ((Console.CursorLeft != 0))
                Console.WriteLine();
        }
        catch { }
    }
}
