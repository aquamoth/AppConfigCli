using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AppConfigCli;
using AppConfigCli.Core;
using FluentAssertions;
using Xunit;
using ItemState = AppConfigCli.ItemState;

public class ReplaceCommandTests
{
    private static InMemoryConfigRepository SeedRepo()
    {
        return new InMemoryConfigRepository(new[]
        {
            new ConfigEntry { Key = "p:Color", Label = "dev", Value = "red green blue" },
            new ConfigEntry { Key = "p:Greeting", Label = "dev", Value = "Hello 123 Hello" },
            new ConfigEntry { Key = "p:Note", Label = "dev", Value = "unchanged" },
        });
    }

    [Fact]
    public async Task ApplyReplace_replaces_across_visible_values_and_sets_state()
    {
        var repo = SeedRepo();
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        var rx = new Regex("Hello", RegexOptions.Compiled);
        var (items, matches) = AppConfigCli.Editor.Commands.Replace.ApplyReplace(app, rx, "Hi");

        items.Should().Be(1); // Greeting only
        matches.Should().Be(2);

        var greet = app.Test_Items.Single(i => i.ShortKey == "Greeting");
        greet.Value.Should().Be("Hi 123 Hi");
        greet.State.Should().Be(ItemState.Modified);

        var color = app.Test_Items.Single(i => i.ShortKey == "Color");
        color.State.Should().Be(ItemState.Unchanged);
        color.Value.Should().Be("red green blue");
    }

    [Fact]
    public async Task ApplyReplace_uses_capture_groups()
    {
        var repo = new InMemoryConfigRepository(new[]
        {
            new ConfigEntry { Key = "p:Path", Label = "dev", Value = "a/b/c" },
        });
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        var rx = new Regex("([a-z])/([a-z])/([a-z])", RegexOptions.Compiled);
        var result = AppConfigCli.Editor.Commands.Replace.ApplyReplace(app, rx, "$3-$2-$1");
        result.ItemsAffected.Should().Be(1);
        result.TotalMatches.Should().Be(1);
        var item = app.Test_Items.Single(i => i.ShortKey == "Path");
        item.Value.Should().Be("c-b-a");
        item.State.Should().Be(ItemState.Modified);
    }

    [Fact]
    public async Task ApplyReplace_skips_deleted_items()
    {
        var repo = SeedRepo();
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        var note = app.Test_Items.Single(i => i.ShortKey == "Note");
        note.State = ItemState.Deleted;

        var rx = new Regex("unchanged", RegexOptions.Compiled);
        var r = AppConfigCli.Editor.Commands.Replace.ApplyReplace(app, rx, "changed");
        r.ItemsAffected.Should().Be(0);
        r.TotalMatches.Should().Be(0);
        note.Value.Should().Be("unchanged");
        note.State.Should().Be(ItemState.Deleted);
    }
}
