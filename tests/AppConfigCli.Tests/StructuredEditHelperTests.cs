using System.Collections.Generic;
using System.Linq;
using AppConfigCli;
using FluentAssertions;
using Xunit;

public class StructuredEditHelperTests
{
    private static List<Item> SeedDev()
    {
        return new List<Item>
        {
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "dev", OriginalValue = "red", Value = "red", State = ItemState.Unchanged },
            new Item { FullKey = "p:Title", ShortKey = "Title", Label = "dev", OriginalValue = "Hello", Value = "Hello", State = ItemState.Unchanged },
        };
    }

    [Fact]
    public void ApplyJson_invalid_top_level_returns_error()
    {
        var items = SeedDev();
        var visible = items.Where(i => i.Label == "dev");
        var json = "[1,2,3]"; // array, not object

        var (ok, err, c, u, d) = StructuredEditHelper.ApplyJsonEdits(json, ":", items, visible, "p:", "dev");
        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
        c.Should().Be(0); u.Should().Be(0); d.Should().Be(0);
    }

    [Fact]
    public void ApplyJson_creates_updates_and_deletes()
    {
        var items = SeedDev();
        var visible = items.Where(i => i.Label == "dev");
        // Delete Title (omit), update Color, create NewKey
        var json = "{\n  \"Color\": \"blue\",\n  \"NewKey\": \"val\"\n}";

        var (ok, err, c, u, d) = StructuredEditHelper.ApplyJsonEdits(json, ":", items, visible, "p:", "dev");
        ok.Should().BeTrue(err);
        c.Should().Be(1); u.Should().Be(1); d.Should().Be(1);

        items.Should().Contain(i => i.FullKey == "p:NewKey" && i.Label == "dev" && i.State == ItemState.New && i.Value == "val");
        items.Single(i => i.FullKey == "p:Color" && i.Label == "dev").Value.Should().Be("blue");
        items.Single(i => i.FullKey == "p:Title" && i.Label == "dev").State.Should().Be(ItemState.Deleted);
    }

    [Fact]
    public void ApplyYaml_invalid_malformed_returns_error()
    {
        var items = SeedDev();
        var visible = items.Where(i => i.Label == "dev");
        var yaml = "key: [unterminated"; // malformed YAML

        var (ok, err, c, u, d) = StructuredEditHelper.ApplyYamlEdits(yaml, ":", items, visible, "p:", "dev");
        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
        c.Should().Be(0); u.Should().Be(0); d.Should().Be(0);
    }

    [Fact]
    public void ApplyYaml_creates_updates_and_deletes()
    {
        var items = SeedDev();
        var visible = items.Where(i => i.Label == "dev");
        // YAML mapping: delete Title, update Color, add NewKey
        var yaml = "Color: blue\nNewKey: val\n";

        var (ok, err, c, u, d) = StructuredEditHelper.ApplyYamlEdits(yaml, ":", items, visible, "p:", "dev");
        ok.Should().BeTrue(err);
        c.Should().Be(1); u.Should().Be(1); d.Should().Be(1);

        items.Should().Contain(i => i.FullKey == "p:NewKey" && i.Label == "dev" && i.State == ItemState.New && i.Value == "val");
        items.Single(i => i.FullKey == "p:Color" && i.Label == "dev").Value.Should().Be("blue");
        items.Single(i => i.FullKey == "p:Title" && i.Label == "dev").State.Should().Be(ItemState.Deleted);
    }
}

