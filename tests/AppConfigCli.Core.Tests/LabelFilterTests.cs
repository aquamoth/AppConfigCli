using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class LabelFilterTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "\0")]
    [InlineData("dev", "dev")]
    [InlineData("prod", "prod")]
    public void ForSelector_maps_correctly(string? input, string? expected)
    {
        LabelFilter.ForSelector(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("dev", "dev")]
    [InlineData("prod", "prod")]
    public void ForWrite_maps_correctly(string? input, string? expected)
    {
        LabelFilter.ForWrite(input).Should().Be(expected);
    }
}

