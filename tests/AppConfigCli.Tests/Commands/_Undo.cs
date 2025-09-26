using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppConfigCli;
using AppConfigCli.Core;
using AppConfigCli.Editor.Commands;
using FluentAssertions;
using Xunit;

public partial class _Commands
{
    public class _Undo
    {
        private static InMemoryConfigRepository SeedRepo()
        {
            // Prefix p:, three keys A/B/C under two labels: original and hidden, plus unlabeled
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
        public async Task undo_all_on_unmodified_items_does_nothing_and_keeps_label()
        {
            // Arrange
            var repo = SeedRepo();
            var consoleEx = new TestConsoleEx();
            var app = await InstrumentedEditorApp(repo, "original", consoleEx);

            // Sanity: visible rows for label: original
            var visibleBefore = app.GetVisibleItems();
            visibleBefore.Should().HaveCount(3);
            visibleBefore.Select(i => i.ShortKey).Should().BeEquivalentTo(new[] { "A", "B", "C" });
            var beforeSnapshot = visibleBefore
                .ToDictionary(i => i.ShortKey, i => (Value: i.Value, State: i.State));

            // Act: invoke the Undo command for 'all'
            consoleEx.EnqueueInput(Environment.NewLine); // satisfy "Press Enter to continue..."
            var cmd = new Undo(-1, -1); // -1/-1 maps to "all"
            await cmd.ExecuteAsync(app);

            // Assert: label filter should still be "original"
            app.Label.Should().Be("original");

            // Visible items should be unchanged
            var visibleAfter = app.GetVisibleItems();
            visibleAfter.Should().HaveCount(3);
            visibleAfter.Select(i => i.ShortKey).Should().BeEquivalentTo(new[] { "A", "B", "C" });
            foreach (var it in visibleAfter)
            {
                var snap = beforeSnapshot[it.ShortKey];
                it.Value.Should().Be(snap.Value);
                it.State.Should().Be(AppConfigCli.ItemState.Unchanged);
            }

            // Repository contents should remain the same for all labels
            var origAfter = await repo.ListAsync("p:", "original");
            origAfter.Should().HaveCount(3);
            origAfter.First(e => e.Key == "p:A").Value.Should().Be("origA");
            origAfter.First(e => e.Key == "p:B").Value.Should().Be("origB");
            origAfter.First(e => e.Key == "p:C").Value.Should().Be("origC");

            var hiddenAfter = await repo.ListAsync("p:", "hidden");
            hiddenAfter.Should().HaveCount(3);
            hiddenAfter.First(e => e.Key == "p:A").Value.Should().Be("hidA");
            hiddenAfter.First(e => e.Key == "p:B").Value.Should().Be("hidB");
            hiddenAfter.First(e => e.Key == "p:C").Value.Should().Be("hidC");
        }
    }
}
