using AppConfigCli;
using FluentAssertions;
using Xunit;

public class _HistoryNavigator
{
    [Fact]
    public void up_down_preserves_bottom_draft()
    {
        var history = new System.Collections.Generic.List<string> { "a", "b" };
        var nav = new HistoryNavigator(history);

        // Start with a draft
        nav.TypeChar('x');
        nav.Text.Should().Be("x");

        // Up to last command
        nav.Up();
        Assert.Equal("b", nav.Text);

        // Up to previous
        nav.Up();
        nav.Text.Should().Be("a");

        // Down to next
        nav.Down();
        nav.Text.Should().Be("b");

        // Down to bottom should restore draft
        nav.Down();
        nav.Text.Should().Be("x");
    }

    [Fact]
    public void editing_recalled_command_switches_to_bottom_and_keeps_edits()
    {
        var history = new System.Collections.Generic.List<string> { "a", "b" };
        var nav = new HistoryNavigator(history);

        // Recall last command
        nav.Up();
        nav.Text.Should().Be("b");

        // Edit recalled command -> becomes draft at bottom
        nav.TypeChar('!');
        nav.Text.Should().Be("b!");

        // Move up (back into history) then down, should return to edited draft
        nav.Up();
        Assert.Equal("b", nav.Text);
        nav.Down();
        Assert.Equal("b!", nav.Text);
    }

    [Fact]
    public void backspace_edit_on_recalled_switches_to_bottom()
    {
        var history = new System.Collections.Generic.List<string> { "cmd" };
        var nav = new HistoryNavigator(history);
        nav.Up();
        nav.Text.Should().Be("cmd");
        nav.Backspace();
        nav.Text.Should().Be("cm");

        // Up then down restores edited draft
        nav.Up();
        nav.Text.Should().Be("cmd");
        nav.Down();
        nav.Text.Should().Be("cm");
    }
}
