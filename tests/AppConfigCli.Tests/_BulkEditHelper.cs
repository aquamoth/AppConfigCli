using System.Collections.Generic;
using System.Linq;
using AppConfigCli;
using FluentAssertions;
using Xunit;

public class _BulkEditHelper
{
    private static List<Item> Seed()
    {
        return new List<Item>
        {
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "dev", OriginalValue = "red", Value = "red", State = ItemState.Unchanged },
            new Item { FullKey = "p:Title", ShortKey = "Title", Label = "dev", OriginalValue = "Hello", Value = "Hello", State = ItemState.Unchanged },
            new Item { FullKey = "p:Old", ShortKey = "Old", Label = "dev", OriginalValue = "gone", Value = "gone", State = ItemState.Unchanged },
        };
    }

    [Fact]
    public void ApplyEdits_creates_updates_and_deletes()
    {
        var items = Seed();
        var visible = items.Where(i => i.Label == "dev").ToList();
        var content = "# header\nColor\tblue\nTitle\tHello World\n"; // remove Old, modify Title, update Color

        var (created, updated, deleted) = BulkEditHelper.ApplyEdits(content, items, visible, "p:", "dev");

        created.Should().Be(0);
        updated.Should().Be(2);
        deleted.Should().Be(1);

        var color = items.Single(i => i.ShortKey == "Color" && i.Label == "dev");
        color.Value.Should().Be("blue");
        color.State.Should().Be(ItemState.Modified);

        var title = items.Single(i => i.ShortKey == "Title" && i.Label == "dev");
        title.Value.Should().Be("Hello World");
        title.State.Should().Be(ItemState.Modified);

        items.Should().NotContain(i => i.ShortKey == "Old" && i.Label == "dev" && i.State != ItemState.Deleted);
    }

    [Fact]
    public void ApplyEdits_adds_new_item()
    {
        var items = Seed();
        var visible = items.Where(i => i.Label == "dev").ToList();
        var content = "Color\tred\nTitle\tHello\nNewKey\tval\n"; // add NewKey, keep others same

        var (created, updated, deleted) = BulkEditHelper.ApplyEdits(content, items, visible, "p:", "dev");

        created.Should().Be(1);
        updated.Should().Be(2); // Color/Title re-written, considered update path
        deleted.Should().Be(1);

        items.Should().Contain(i => i.ShortKey == "NewKey" && i.Label == "dev" && i.State == ItemState.New && i.Value == "val");
    }
}
