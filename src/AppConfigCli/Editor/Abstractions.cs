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

// Console abstraction for safer rendering and input in tests
internal interface IConsoleEx
{
    int WindowWidth { get; }
    int WindowHeight { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    bool TreatControlCAsInput { get; set; }
    bool KeyAvailable { get; }
    void SetCursorPosition(int left, int top);
    void Clear();
    void Write(string text);
    void Write(char ch);
    void WriteLine(string text);
    void WriteLine();
    ConsoleKeyInfo ReadKey(bool intercept);
    string? ReadLine();
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
    public ConsoleColor BackgroundColor
    {
        get { try { return Console.BackgroundColor; } catch { return ConsoleColor.Black; } }
        set { try { Console.BackgroundColor = value; } catch { } }
    }
    public bool TreatControlCAsInput
    {
        get { try { return Console.TreatControlCAsInput; } catch { return false; } }
        set { try { Console.TreatControlCAsInput = value; } catch { } }
    }
    public bool KeyAvailable { get { try { return Console.KeyAvailable; } catch { return false; } } }
    public void SetCursorPosition(int left, int top) { try { Console.SetCursorPosition(left, top); } catch { } }
    public void Clear() { try { Console.Clear(); } catch { } }
    public void Write(string text) { try { Console.Write(text); } catch { } }
    public void Write(char ch) { try { Console.Write(ch); } catch { } }
    public void WriteLine(string text)
    {
        try
        {
            Console.Write(text);
            if ((Console.CursorLeft != 0))
                Console.WriteLine();
        }
        catch { }
    }
    public void WriteLine() { try { Console.WriteLine(); } catch { } }
    public ConsoleKeyInfo ReadKey(bool intercept) { try { return Console.ReadKey(intercept); } catch { return new ConsoleKeyInfo('\0', 0, false, false, false); } }
    public string? ReadLine() { try { return Console.ReadLine(); } catch { return null; } }
}
