using System.Collections.Generic;
using System.Linq;
using AppConfigCli;
using FluentAssertions;
using Xunit;

public class _StructuredEditHelper
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
    public void build_json_does_not_escape_ascii_in_values()
    {
        // Arrange: sample similar to the user-provided example
        var items = new List<Item>
        {
            new Item { FullKey = "MyConfigSection:SampleConnectionString", ShortKey = "MyConfigSection:SampleConnectionString", Label = "prod", OriginalValue = null, Value = "Data Source=database.example.com;Port=12345;Uid=dbuser;Password=my-Super+Secret/pa$$_w0rd#=;charset=utf8", State = ItemState.Unchanged },
            new Item { FullKey = "MyConfigSection:Test", ShortKey = "MyConfigSection:Test", Label = "prod", OriginalValue = null, Value = "ConnectionString in prod mode. ", State = ItemState.Unchanged },
            new Item { FullKey = "Settings:BackgroundColor", ShortKey = "Settings:BackgroundColor", Label = "prod", OriginalValue = null, Value = "brown", State = ItemState.Unchanged },
            new Item { FullKey = "Settings:Message", ShortKey = "Settings:Message", Label = "prod", OriginalValue = null, Value = "Hello in Production override mode !!!!", State = ItemState.Unchanged },
        };

        // Visible under a single label as required by json editor flow
        var visible = items.Where(i => i.Label == "prod");

        // Act
        var json = StructuredEditHelper.BuildJsonContent(visible, ":");

        // Assert: ensure '+' and other ASCII are not unicode-escaped
        json.Should().Contain("my-Super+Secret/pa$$_w0rd#=");
        json.Should().NotContain("\\u002B"); // '+'
        json.Should().NotContain("\\u002F"); // '/'
        json.Should().NotContain("\\u0023"); // '#'

        // Roundtrip apply should keep the exact value
        var (ok, err, created, updated, deleted) = StructuredEditHelper.ApplyJsonEdits(json, ":", items, visible, prefix: string.Empty, activeLabel: "prod");
        ok.Should().BeTrue(err);
        items.Should().Contain(i => i.FullKey == "MyConfigSection:SampleConnectionString" && i.Label == "prod" && i.Value!.Contains("+Secret/"));
    }

    [Fact]
    public void build_json_does_not_escape_ascii_in_property_names()
    {
        var items = new List<Item>
        {
            new Item { FullKey = "Foo+Bar:Baz", ShortKey = "Foo+Bar:Baz", Label = "prod", OriginalValue = null, Value = "v", State = ItemState.Unchanged }
        };
        var visible = items.Where(i => i.Label == "prod");
        var json = StructuredEditHelper.BuildJsonContent(visible, ":");

        // Property name should appear literally with '+'
        json.Should().Contain("\"Foo+Bar\"");
        json.Should().NotContain("\\u002B");
    }

    [Fact]
    public void apply_json_invalid_top_level_returns_error()
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
    public void apply_json_creates_updates_and_deletes()
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
    public void apply_yaml_invalid_malformed_returns_error()
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
    public void apply_yaml_creates_updates_and_deletes()
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

    [Fact]
    public void apply_yaml_preserves_ascii_plus_and_slash_in_values()
    {
        // Arrange: start with a mismatched value so ApplyYamlEdits updates it
        var items = new List<Item>
        {
            new Item { FullKey = "MyConfigSection:SampleConnectionString", ShortKey = "MyConfigSection:SampleConnectionString", Label = "prod", OriginalValue = null, Value = "WRONG", State = ItemState.Unchanged },
            new Item { FullKey = "Settings:BackgroundColor", ShortKey = "Settings:BackgroundColor", Label = "prod", OriginalValue = null, Value = "brown", State = ItemState.Unchanged },
            new Item { FullKey = "Settings:Message", ShortKey = "Settings:Message", Label = "prod", OriginalValue = null, Value = "Hello", State = ItemState.Unchanged },
        };
        var visible = items.Where(i => i.Label == "prod");

        var desired = "Data Source=database.example.com;Port=12345;Uid=dbuser;Password=my-Super+Secret/pa$$_w0rd#=;charset=utf8";
        var yaml =
            "MyConfigSection:\n" +
            "  SampleConnectionString: \"" + desired + "\"\n" +
            "Settings:\n" +
            "  BackgroundColor: brown\n" +
            "  Message: Hello in Production override mode !!!!\n";

        // Act
        var (ok, err, c, u, d) = StructuredEditHelper.ApplyYamlEdits(yaml, ":", items, visible, prefix: string.Empty, activeLabel: "prod");

        // Assert
        ok.Should().BeTrue(err);
        u.Should().BeGreaterThan(0);
        items.Should().Contain(i => i.FullKey == "MyConfigSection:SampleConnectionString" && i.Label == "prod" && i.Value == desired);
        // Ensure '+' and '/' are present literally in the resulting value
        items.Single(i => i.FullKey == "MyConfigSection:SampleConnectionString" && i.Label == "prod").Value!.Should().Contain("+Secret/");
    }

    [Fact]
    public void apply_yaml_supports_plus_in_property_names()
    {
        var items = new List<Item>();
        var visible = items.Where(i => i.Label == "prod");
        var yaml =
            "Foo+Bar:\n" +
            "  Baz: v\n";

        var (ok, err, c, u, d) = StructuredEditHelper.ApplyYamlEdits(yaml, ":", items, visible, prefix: string.Empty, activeLabel: "prod");
        ok.Should().BeTrue(err);
        c.Should().Be(1);
        items.Should().Contain(i => i.ShortKey == "Foo+Bar:Baz" && i.Value == "v");
    }
}
