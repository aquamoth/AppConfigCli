namespace AppConfigCli.Core;

public abstract record Command
{
    public sealed record Add() : Command;
    public sealed record Edit(int Index) : Command;
    public sealed record Delete(int Start, int End) : Command;
    public sealed record Copy(int Start, int End) : Command;
    public sealed record Label(string? Value, bool Clear, bool Empty) : Command; // Clear: no args, Empty: '-'
    public sealed record Grep(string? Pattern, bool Clear) : Command;
    public sealed record Save() : Command;
    public sealed record Reload() : Command;
    public sealed record Quit() : Command;
    public sealed record Help() : Command;
    public sealed record Open() : Command;
    public sealed record Prefix(string? Value, bool Prompt) : Command; // Prompt when no arg
    public sealed record UndoRange(int Start, int End) : Command;
    public sealed record UndoAll() : Command;
    public sealed record Json(string Separator) : Command;
    public sealed record Yaml(string Separator) : Command;
    public sealed record WhoAmI() : Command;
}

