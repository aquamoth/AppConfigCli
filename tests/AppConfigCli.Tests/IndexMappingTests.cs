using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AppConfigCli;
using FluentAssertions;
using Xunit;

public class IndexMappingTests
{
    private static EditorApp MakeAppWithItems()
    {
        // Repo not used by MapVisibleRangeToItemIndices; provide a minimal in-memory repo
        var repo = new AppConfigCli.Core.InMemoryConfigRepository(new List<AppConfigCli.Core.ConfigEntry>());
        var app = new EditorApp(repo, "p:", null);
        app.Test_Items.AddRange(new[]
        {
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "dev", OriginalValue = "red", Value = "red", State = ItemState.Unchanged },
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "prod", OriginalValue = "blue", Value = "blue", State = ItemState.Unchanged },
            new Item { FullKey = "p:Title", ShortKey = "Title", Label = null, OriginalValue = "Hello", Value = "Hello", State = ItemState.Unchanged },
        });
        return app;
    }

    [Fact]
    public void Maps_single_index_over_any_label()
    {
        var app = MakeAppWithItems();
        var idx1 = app.MapVisibleRangeToItemIndices(1, 1, out var err1);
        err1.Should().BeEmpty();
        idx1.Should().Equal(new[] { 0 });
        var idx2 = app.MapVisibleRangeToItemIndices(2, 2, out var err2);
        err2.Should().BeEmpty();
        idx2.Should().Equal(new[] { 1 });
    }

    [Fact]
    public void Respects_label_filter_and_unlabeled()
    {
        var app = MakeAppWithItems();
        app.Label = "dev";
        var devIdx = app.MapVisibleRangeToItemIndices(1, 1, out var e1);
        e1.Should().BeEmpty();
        devIdx.Should().Equal(new[] { 0 });

        app.Label = ""; // unlabeled only
        var unlIdx = app.MapVisibleRangeToItemIndices(1, 1, out var e2);
        e2.Should().BeEmpty();
        unlIdx.Should().Equal(new[] { 2 });
    }

    [Fact]
    public void Respects_key_regex()
    {
        var app = MakeAppWithItems();
        app.KeyRegex = new Regex("^C", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var idx1 = app.MapVisibleRangeToItemIndices(1, 1, out var err1);
        err1.Should().BeEmpty();
        idx1.Should().Equal(new[] { 0 });
        var idx2 = app.MapVisibleRangeToItemIndices(2, 2, out var err2);
        err2.Should().BeEmpty();
        idx2.Should().Equal(new[] { 1 });
    }

    [Fact]
    public void Out_of_range_returns_error()
    {
        var app = MakeAppWithItems();
        var idx = app.MapVisibleRangeToItemIndices(4, 4, out var err);
        idx.Should().BeNull();
        err.Should().Be("Index out of range.");
    }
}

