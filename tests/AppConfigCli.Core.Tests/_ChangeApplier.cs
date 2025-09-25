using System.Collections.Generic;
using System.Linq;
using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class _ChangeApplier
{
    [Fact]
    public void unchanged_items_produce_no_changes()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "app:Color", ShortKey = "Color", Label = "dev", OriginalValue = "blue", Value = "blue", State = ItemState.Unchanged }
        };
        var changes = ChangeApplier.Compute(items);
        changes.Upserts.Should().BeEmpty();
        changes.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void new_and_modified_become_upserts()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "app:Title", ShortKey = "Title", Label = "dev", OriginalValue = null, Value = "Hello", State = ItemState.New },
            new Item { FullKey = "app:Color", ShortKey = "Color", Label = "prod", OriginalValue = "blue", Value = "red", State = ItemState.Modified },
        };
        var changes = ChangeApplier.Compute(items);
        changes.Upserts.Should().HaveCount(2);
        changes.Upserts.Should().BeEquivalentTo(new[]
        {
            new ConfigEntry { Key = "app:Title", Label = "dev", Value = "Hello" },
            new ConfigEntry { Key = "app:Color", Label = "prod", Value = "red" },
        });
        changes.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void deleted_becomes_delete_with_label_mapping()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "app:Count", ShortKey = "Count", Label = "", OriginalValue = "1", Value = "1", State = ItemState.Deleted },
        };
        var changes = ChangeApplier.Compute(items);
        changes.Upserts.Should().BeEmpty();
        changes.Deletes.Should().ContainSingle();
        var del = changes.Deletes.Single();
        del.Key.Should().Be("app:Count");
        del.Label.Should().BeNull(); // empty label mapped to null for writes
    }

    [Fact]
    public void duplicate_delete_and_modify_results_in_single_upsert()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "app:Color", ShortKey = "Color", Label = "dev", OriginalValue = "blue", Value = "blue", State = ItemState.Deleted },
            new Item { FullKey = "app:Color", ShortKey = "Color", Label = "dev", OriginalValue = "blue", Value = "red", State = ItemState.Modified },
        };
        var changes = ChangeApplier.Compute(items);
        changes.Upserts.Should().ContainSingle();
        changes.Upserts[0].Should().BeEquivalentTo(new ConfigEntry { Key = "app:Color", Label = "dev", Value = "red" });
        changes.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void last_new_wins_among_duplicates()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "app:Title", ShortKey = "Title", Label = null, OriginalValue = null, Value = "Hello", State = ItemState.New },
            new Item { FullKey = "app:Title", ShortKey = "Title", Label = null, OriginalValue = null, Value = "Hello World", State = ItemState.New },
        };
        var changes = ChangeApplier.Compute(items);
        changes.Upserts.Should().ContainSingle();
        changes.Upserts[0].Should().BeEquivalentTo(new ConfigEntry { Key = "app:Title", Label = null, Value = "Hello World" });
        changes.Deletes.Should().BeEmpty();
    }
}
