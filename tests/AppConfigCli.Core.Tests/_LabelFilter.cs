using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class _LabelFilter
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "\0")]
    [InlineData("dev", "dev")]
    [InlineData("prod", "prod")]
    public void for_selector_maps_correctly(string? input, string? expected)
    {
        LabelFilter.ForSelector(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("dev", "dev")]
    [InlineData("prod", "prod")]
    public void for_write_maps_correctly(string? input, string? expected)
    {
        LabelFilter.ForWrite(input).Should().Be(expected);
    }
}
