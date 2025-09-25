using System.Collections.Generic;
using System.Text.RegularExpressions;
using AppConfigCli;
using FluentAssertions;
using Xunit;

public class _RangeMapper
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
    public void maps_any_label_range()
    {
        var src = Sample();
        var idx = RangeMapper.Map(src, null, null, 1, 2, out var error);
        error.Should().BeEmpty();
        idx.Should().Equal(new[] { 0, 1 });
    }

    [Fact]
    public void maps_literal_label_and_regex()
    {
        var src = Sample();
        var re = new Regex("^C", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var idx = RangeMapper.Map(src, "dev", re, 2, 2, out var error);
        error.Should().BeEmpty();
        idx.Should().Equal(new[] { 3 });
    }
}
