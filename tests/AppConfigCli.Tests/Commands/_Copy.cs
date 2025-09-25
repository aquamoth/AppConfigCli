using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppConfigCli;
using AppConfigCli.Core;
using AppConfigCli.Editor.Abstractions;
using AppConfigCli.Editor.Commands;
using FluentAssertions;
using Xunit;

public partial class _Commands
{
    internal static readonly TestConsoleEx consoleEx = new TestConsoleEx();

    internal static async Task<EditorApp> InstrumentedEditorApp(InMemoryConfigRepository repo, string label)
    {
        var app = new EditorApp(
            repo,
            prefix: "p:",
            label,
            () => Task.CompletedTask,
            new DefaultFileSystem(),
            new DefaultExternalEditor(),
            ConsoleTheme.Load(),
            consoleEx);

        await app.LoadAsync();
        return app;
    }

    public class _Copy
    {
        private static InMemoryConfigRepository SeedRepo()
        {
            // Prefix p:, two keys A/B under two labels: original and hidden
            var entries = new[]
            {
            new ConfigEntry { Key = "p:A", Label = null, Value = "nullA" },
            new ConfigEntry { Key = "p:B", Label = null, Value = "nullB" },
            new ConfigEntry { Key = "p:C", Label = null, Value = "nullC" },
            new ConfigEntry { Key = "p:A", Label = "original", Value = "origA" },
            new ConfigEntry { Key = "p:B", Label = "original", Value = "origB" },
            new ConfigEntry { Key = "p:C", Label = "original", Value = "origC" },
            new ConfigEntry { Key = "p:A", Label = "hidden",   Value = "hidA"  },
            new ConfigEntry { Key = "p:B", Label = "hidden",   Value = "hidB"  },
            new ConfigEntry { Key = "p:C", Label = "hidden",   Value = "hidC"  },
        }.ToList();

            return new InMemoryConfigRepository(entries);
        }

        [Fact]
        public async Task does_not_affect_other_labels_and_switches_to_the_new_label()
        {
            // Arrange
            var repo = SeedRepo();
            var app = await InstrumentedEditorApp(repo, "original");

            // Sanity: only two visible rows (label: original)
            var visible = app.GetVisibleItems();
            visible.Should().HaveCount(3);
            visible.Select(i => i.ShortKey).Should().BeEquivalentTo(new[] { "A", "B", "C" });

            // Capture the hidden label originals for later comparison
            var hiddenBefore = await repo.ListAsync("p:", "hidden");
            hiddenBefore.Should().HaveCount(3);
            var hidA = hiddenBefore.First(e => e.Key == "p:A").Value;
            var hidB = hiddenBefore.First(e => e.Key == "p:B").Value;
            var hidC = hiddenBefore.First(e => e.Key == "p:C").Value;

            // Prepare console input for the Copy prompt: target label name
            consoleEx.EnqueueInput("target" + Environment.NewLine);

            // Act: invoke the Copy command for range 1-2
            var cmd = new Copy(1, 2);
            await cmd.ExecuteAsync(app);

            // Assert: label filter should now be "target" (not cleared to null/any)
            app.Label.Should().Be("target");

            // Only "target" items should be visible now
            var visibleAfter = app.GetVisibleItems();
            visibleAfter.Should().HaveCount(2);
            visibleAfter.Should().OnlyContain(i => i.Label == "target");

            // Hidden items in the repository must remain unchanged (no overwrite)
            var hiddenAfter = await repo.ListAsync("p:", "hidden");
            hiddenAfter.Should().HaveCount(3);
            hiddenAfter.First(e => e.Key == "p:A").Value.Should().Be(hidA);
            hiddenAfter.First(e => e.Key == "p:B").Value.Should().Be(hidB);
            hiddenAfter.First(e => e.Key == "p:C").Value.Should().Be(hidC);

            // New "target" items should exist in the in-memory app state (unsaved, New state)
            var targetItems = app.Items.Where(i => i.Label == "target").ToList();
            targetItems.Should().HaveCount(2);
            targetItems.Should().OnlyContain(i => i.State == AppConfigCli.ItemState.New);
            targetItems.Select(i => i.ShortKey).Should().BeEquivalentTo(new[] { "A", "B" });
        }
    }
}