using System;
using System.Collections.Generic;
using System.Text;

namespace AppConfigCli.Editor.Abstractions;

internal sealed class DefaultConsoleEx : IConsoleEx
{
    private volatile bool hasPendingCtrlC = false;

    public DefaultConsoleEx()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;   // don’t terminate the process
            hasPendingCtrlC = true;      // record the fact
        };
    }

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
    public bool KeyAvailable { get { try { return hasPendingCtrlC || Console.KeyAvailable; } catch { return false; } } }
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
    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (hasPendingCtrlC)
        {
            hasPendingCtrlC = false;
            return new ConsoleKeyInfo('\x3', ConsoleKey.C, false, false, true); // Ctrl-C
        }

        try
        {
            return Console.ReadKey(intercept);
        }
        catch
        {
            return new ConsoleKeyInfo('\0', 0, false, false, false);
        }
    }
    public string? ReadLine() { try { return Console.ReadLine(); } catch { return null; } }

    internal void ErrorWriteLine(string v)
    {
        Console.Error.WriteLine(v);
    }
}
