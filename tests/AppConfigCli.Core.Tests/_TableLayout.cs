using System.Collections.Generic;
using AppConfigCli.Core;
using AppConfigCli.Core.UI;
using FluentAssertions;
using Xunit;

public class _TableLayout
{
    private static List<Item> ItemsWithLabels()
    {
        return new List<Item>
        {
            new Item { FullKey = "p:Color", ShortKey = "Color", Label = "dev", OriginalValue = "red", Value = "red", State = ItemState.Unchanged },
            new Item { FullKey = "p:Title", ShortKey = "Title", Label = null, OriginalValue = "Hello", Value = "Hello", State = ItemState.Unchanged },
            new Item { FullKey = "p:SomeVeryLongKeyName", ShortKey = "SomeVeryLongKeyName", Label = "production", OriginalValue = "x", Value = "x", State = ItemState.Unchanged },
        };
    }

    [Fact]
    public void compute_without_value_allocates_key_and_label()
    {
        var items = ItemsWithLabels();
        TableLayout.Compute(totalWidth: 50, includeValue: false, items,
            out var keyW, out var labelW, out var valueW);

        valueW.Should().Be(0);
        labelW.Should().BeGreaterOrEqualTo(8);
        keyW.Should().BeGreaterOrEqualTo(15);
        (keyW + labelW + 10).Should().BeLessOrEqualTo(50);
    }

    [Fact]
    public void compute_with_value_reserves_min_value_space()
    {
        var items = ItemsWithLabels();
        TableLayout.Compute(totalWidth: 80, includeValue: true, items,
            out var keyW, out var labelW, out var valueW);

        keyW.Should().BeGreaterOrEqualTo(15);
        labelW.Should().BeInRange(8, 25);
        valueW.Should().BeGreaterOrEqualTo(10);
        (keyW + labelW + valueW + 12).Should().BeLessOrEqualTo(80);
    }

    [Fact]
    public void compute_with_value_at_threshold_maintains_minimums()
    {
        var items = ItemsWithLabels();
        TableLayout.Compute(totalWidth: 60, includeValue: true, items,
            out var keyW, out var labelW, out var valueW);

        keyW.Should().BeGreaterOrEqualTo(15);
        valueW.Should().BeGreaterOrEqualTo(10);
        (keyW + labelW + valueW + 12).Should().BeLessOrEqualTo(60);
    }

    [Fact]
    public void compute_with_value_accounts_for_four_digit_index_width()
    {
        // Simulate 1000 visible items to force a 4-digit index column
        var items = new List<Item>();
        for (int i = 0; i < 1000; i++)
        {
            items.Add(new Item { FullKey = $"p:Key{i}", ShortKey = $"Key{i}", Label = i % 2 == 0 ? "dev" : null, OriginalValue = "v", Value = "v", State = ItemState.Unchanged });
        }

        TableLayout.Compute(totalWidth: 80, includeValue: true, items,
            out var keyW, out var labelW, out var valueW);

        int indexDigits = 4; // 1000 -> 4 digits
        keyW.Should().BeGreaterOrEqualTo(15);
        labelW.Should().BeInRange(8, 25);
        valueW.Should().BeGreaterOrEqualTo(10);
        // fixed = digits + 9 when value column is included
        (keyW + labelW + valueW + (indexDigits + 9)).Should().BeLessOrEqualTo(80);
    }

    [Fact]
    public void compute_without_value_accounts_for_four_digit_index_width()
    {
        var items = new List<Item>();
        for (int i = 0; i < 1000; i++)
        {
            items.Add(new Item { FullKey = $"p:Key{i}", ShortKey = $"Key{i}", Label = i % 2 == 0 ? "dev" : null, OriginalValue = "v", Value = "v", State = ItemState.Unchanged });
        }

        TableLayout.Compute(totalWidth: 50, includeValue: false, items,
            out var keyW, out var labelW, out var valueW);

        int indexDigits = 4;
        valueW.Should().Be(0);
        labelW.Should().BeGreaterOrEqualTo(8);
        keyW.Should().BeGreaterOrEqualTo(15);
        // fixed = digits + 7 when no value column
        (keyW + labelW + (indexDigits + 7)).Should().BeLessOrEqualTo(50);
    }
}
