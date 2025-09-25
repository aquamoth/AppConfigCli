using AppConfigCli.Editor.Abstractions;
using System;
using System.Collections.Concurrent;

namespace AppConfigCli;

internal sealed class TestConsoleEx : IConsoleEx
{
    private readonly ConcurrentQueue<string> _input = new();
    private readonly System.Text.StringBuilder _out = new();

    public int WindowWidth { get; set; } = 100;
    public int WindowHeight { get; set; } = 40;
    public int CursorLeft { get; private set; }
    public int CursorTop { get; private set; }
    public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
    public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

    public bool KeyAvailable => false; // not used in tests that feed ReadLine

    public void EnqueueInput(string line) => _input.Enqueue(line);

    public void SetCursorPosition(int left, int top) { CursorLeft = left; CursorTop = top; }
    public void Clear() { /* no-op */ }
    public void Write(string text) { _out.Append(text); }
    public void Write(char ch) { _out.Append(ch); }
    public void WriteLine(string text) { _out.Append(text); _out.AppendLine(); }
    public void WriteLine() { _out.AppendLine(); }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        // Minimal implementation for engine paths; not used in these tests
        return new ConsoleKeyInfo('\n', ConsoleKey.Enter, false, false, false);
    }

    public string? ReadLine()
    {
        if (_input.TryDequeue(out var s)) return s;
        return string.Empty; // default empty
    }

    public override string ToString() => _out.ToString();
}
