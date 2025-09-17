using AppConfigCli.Core.UI;
using FluentAssertions;
using Xunit;

public class TextTruncationTests
{
    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello", 5, "hello")]
    [InlineData("hello", 4, "hel…")]
    [InlineData("abcdef", 1, "…")]
    [InlineData("abcdef", 0, "")]
    public void TruncateFixed_behaves_as_expected(string s, int width, string expected)
        => TextTruncation.TruncateFixed(s, width).Should().Be(expected);

    [Fact]
    public void PadColumn_truncates_and_pads_to_width()
    {
        TextTruncation.PadColumn("hello", 8).Should().Be("hello   ");
        TextTruncation.PadColumn("longer-than", 5).Should().Be("long…");
    }
}

