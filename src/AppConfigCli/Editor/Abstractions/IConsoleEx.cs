namespace AppConfigCli.Editor.Abstractions;

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
