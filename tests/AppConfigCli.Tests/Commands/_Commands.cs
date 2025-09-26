using System.Threading.Tasks;
using AppConfigCli;
using AppConfigCli.Core;
using AppConfigCli.Editor.Abstractions;

public partial class _Commands
{
    internal static async Task<EditorApp> InstrumentedEditorApp(InMemoryConfigRepository repo, string? label, TestConsoleEx? console = null)
    {
        var app = new EditorApp(
            repo,
            prefix: "p:",
            label,
            () => Task.CompletedTask,
            new DefaultFileSystem(),
            new DefaultExternalEditor(),
            ConsoleTheme.Load(),
            console ?? new TestConsoleEx());

        await app.LoadAsync();
        return app;
    }
}
