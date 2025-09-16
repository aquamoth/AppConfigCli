using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using AppConfigCli.Core;
using Xunit;

public class AppStateReconcilerTests
{
    [Fact]
    public void Modified_matching_server_becomes_Unchanged()
    {
        var prefix = "app:settings:";
        string? label = "dev";

        var local = new List<Item>
        {
            new Item { FullKey = "app:settings:Color", ShortKey = "Color", Label = label, OriginalValue = "blue", Value = "red", State = ItemState.Modified }
        };

        var server = new List<ConfigEntry>
        {
            new ConfigEntry { Key = "app:settings:Color", Label = "dev", Value = "red" }
        };

        var sut = new AppStateReconciler();
        var result = sut.Reconcile(prefix, label, local, server);

        result.Should().HaveCount(1);
        var item = result.Single();
        item.ShortKey.Should().Be("Color");
        item.Value.Should().Be("red");
        item.State.Should().Be(ItemState.Unchanged);
    }

    [Fact]
    public void Modified_deleted_on_server_becomes_New()
    {
        var prefix = "app:settings:";
        string? label = "dev";

        var local = new List<Item>
        {
            new Item { FullKey = "app:settings:Title", ShortKey = "Title", Label = label, OriginalValue = "Hello", Value = "Hello World", State = ItemState.Modified }
        };

        var server = new List<ConfigEntry>();

        var sut = new AppStateReconciler();
        var result = sut.Reconcile(prefix, label, local, server);

        result.Should().HaveCount(1);
        var item = result.Single();
        item.ShortKey.Should().Be("Title");
        item.Value.Should().Be("Hello World");
        item.State.Should().Be(ItemState.New);
    }

    [Fact]
    public void Deleted_that_is_gone_on_server_is_dropped()
    {
        var prefix = "app:settings:";
        string? label = "dev";

        var local = new List<Item>
        {
            new Item { FullKey = "app:settings:Old", ShortKey = "Old", Label = label, OriginalValue = "v1", Value = "v1", State = ItemState.Deleted }
        };

        var server = new List<ConfigEntry>();

        var sut = new AppStateReconciler();
        var result = sut.Reconcile(prefix, label, local, server);

        result.Should().BeEmpty();
    }
}
