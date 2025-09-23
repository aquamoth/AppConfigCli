using System.Linq;
using System.Threading.Tasks;
using AppConfigCli;
using AppConfigCli.Core;
using ItemState = AppConfigCli.ItemState;
using FluentAssertions;
using Xunit;

public class _EditorAppIntegration
{
    private static InMemoryConfigRepository SeedRepo()
    {
        return new InMemoryConfigRepository(new[]
        {
            new ConfigEntry { Key = "p:Color", Label = "dev", Value = "red" },
            new ConfigEntry { Key = "p:Title", Label = "dev", Value = "Hello" },
        });
    }

    [Fact]
    public async Task modify_and_save_updates_repo_and_roundtrips_to_unchanged()
    {
        var repo = SeedRepo();
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        var color = app.Test_Items.Single(i => i.ShortKey == "Color");
        color.Value = "blue";
        if (!color.IsNew) color.State = ItemState.Modified;

        await app.Test_SaveAsync();

        var after = await repo.ListAsync("p:", "dev");
        after.Single(e => e.Key == "p:Color").Value.Should().Be("blue");

        await app.LoadAsync();
        var color2 = app.Test_Items.Single(i => i.ShortKey == "Color");
        color2.State.Should().Be(ItemState.Unchanged);
        color2.Value.Should().Be("blue");
    }

    [Fact]
    public async Task delete_and_save_removes_from_repo()
    {
        var repo = SeedRepo();
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        var title = app.Test_Items.Single(i => i.ShortKey == "Title");
        title.State = ItemState.Deleted;

        await app.Test_SaveAsync();
        var after = await repo.ListAsync("p:", "dev");
        after.Should().OnlyContain(e => e.Key != "p:Title");

        await app.LoadAsync();
        app.Test_Items.Should().OnlyContain(i => i.ShortKey != "Title");
    }

    [Fact]
    public async Task add_new_and_save_inserts_into_repo()
    {
        var repo = SeedRepo();
        var app = new EditorApp(repo, "p:", "dev");
        await app.LoadAsync();

        app.Test_Items.Add(new AppConfigCli.Item { FullKey = "p:New", ShortKey = "New", Label = "dev", OriginalValue = null, Value = "val", State = ItemState.New });

        await app.Test_SaveAsync();
        var after = await repo.ListAsync("p:", "dev");
        after.Should().Contain(e => e.Key == "p:New" && e.Value == "val");

        await app.LoadAsync();
        var added = app.Test_Items.Single(i => i.ShortKey == "New");
        added.State.Should().Be(ItemState.Unchanged);
        added.OriginalValue.Should().Be("val");
        added.Value.Should().Be("val");
    }
}
