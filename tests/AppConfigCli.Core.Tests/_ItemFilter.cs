using System.Collections.Generic;
using System.Text.RegularExpressions;
using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class _ItemFilter
{
    private static List<Item> Sample()
    {
        return new List<Item>
        {
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "dev", Value = "red", OriginalValue = "red", State = ItemState.Unchanged },
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "prod", Value = "blue", OriginalValue = "blue", State = ItemState.Unchanged },
            new Item { FullKey = "p:Title", ShortKey = "Title", Label = null, Value = "Hello", OriginalValue = "Hello", State = ItemState.Unchanged },
            new Item { FullKey = "p:Count", ShortKey = "Count", Label = "dev", Value = "1", OriginalValue = "1", State = ItemState.Unchanged },
        };
    }

    [Fact]
    public void visible_any_label_returns_all()
    {
        var src = Sample();
        var vis = ItemFilter.Visible(src, null, null);
        vis.Should().HaveCount(src.Count);
    }

    [Fact]
    public void visible_unlabeled_returns_only_empty_label()
    {
        var src = Sample();
        var vis = ItemFilter.Visible(src, string.Empty, null);
        vis.Should().ContainSingle();
        vis[0].ShortKey.Should().Be("Title");
        vis[0].Label.Should().BeNull();
    }

    [Fact]
    public void visible_literal_label_returns_only_that_label()
    {
        var src = Sample();
        var vis = ItemFilter.Visible(src, "dev", null);
        vis.Should().HaveCount(2);
        vis.Should().OnlyContain(i => i.Label == "dev");
    }

    [Fact]
    public void visible_applies_regex_on_shortkey()
    {
        var src = Sample();
        var regex = new Regex("^C", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var vis = ItemFilter.Visible(src, null, regex);
        vis.Should().HaveCount(3); // Color (2 labels) + Count
        vis.Should().OnlyContain(i => i.ShortKey.StartsWith("C"));
    }

    [Theory]
    [InlineData(1, 1, new[] { 0 })]
    [InlineData(1, 2, new[] { 0, 1 })]
    [InlineData(2, 3, new[] { 1, 2 })]
    public void map_range_any_label(int start, int end, int[] expected)
    {
        var src = Sample();
        var indices = ItemFilter.MapVisibleRangeToSourceIndices(src, null, null, start, end, out var error);
        error.Should().BeEmpty();
        indices.Should().NotBeNull();
        indices!.Should().Equal(expected);
    }

    [Fact]
    public void map_range_literal_label_and_regex()
    {
        var src = Sample();
        var regex = new Regex("^C", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Visible under dev + regex ^C => Color(dev), Count(dev)
        var indices = ItemFilter.MapVisibleRangeToSourceIndices(src, "dev", regex, 2, 2, out var error);
        error.Should().BeEmpty();
        indices.Should().Equal(new[] { 3 }); // Count(dev) at index 3 in source
    }

    [Fact]
    public void map_range_out_of_bounds_returns_error()
    {
        var src = Sample();
        var indices = ItemFilter.MapVisibleRangeToSourceIndices(src, "prod", null, 2, 2, out var error);
        indices.Should().BeNull();
        error.Should().Be("Index out of range.");
    }

    [Fact]
    public void visible_indices_match_expected_order()
    {
        var src = Sample();
        var regex = new Regex("^C", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var idx = ItemFilter.VisibleIndices(src, null, regex);
        // Expect: Color(dev)=0, Color(prod)=1, Count(dev)=3
        idx.Should().Equal(new[] { 0, 1, 3 });
    }
}
