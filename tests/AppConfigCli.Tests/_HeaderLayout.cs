using FluentAssertions;
using Xunit;

public class _HeaderLayout
{
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    public void all_three_fit_on_one_line_when_space_allows(int width)
    {
        var lines = AppConfigCli.HeaderLayout.Compute(width, "Prefix: p:", "Label: dev", "Filter: user");
        lines.Count.Should().BeGreaterOrEqualTo(1);
        if (width >= 40)
            lines.Count.Should().Be(1);
    }

    [Fact]
    public void two_lines_when_three_do_not_fit()
    {
        int width = 20; // intentionally small
        var lines = AppConfigCli.HeaderLayout.Compute(width, "Prefix: verylongprefix/", "Label: dev", "Filter: hello");
        lines.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void center_label_when_only_label_present()
    {
        int width = 40;
        var lines = AppConfigCli.HeaderLayout.Compute(width, null, "Label: dev", null);
        lines.Count.Should().Be(1);
        var segs = lines[0];
        segs.Should().ContainSingle();
        var seg = segs[0];
        seg.Text.Should().StartWith("Label:");
        seg.Pos.Should().BeGreaterThan(0); // centered not left
    }

    [Fact]
    public void label_center_and_filter_right_when_prefix_hidden_and_space_allows()
    {
        int width = 60;
        var lines = AppConfigCli.HeaderLayout.Compute(width, null, "Label: dev", "Filter: foo");
        lines.Count.Should().Be(1);
        var segs = lines[0];
        segs.Count.Should().Be(2);
        segs[1].Pos.Should().Be(width - segs[1].Text.Length); // right-aligned
    }
}
