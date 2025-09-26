using FluentAssertions;
using Xunit;

public class _LineEditorEngine
{
    [Fact]
    public void insert_and_basic_navigation()
    {
        var e = new AppConfigCli.LineEditorEngine();
        e.SetInitial("");
        foreach (var ch in "hello") e.Insert(ch);
        e.Left(); e.Left(); // hel|lo -> he|llo
        e.Insert('X');
        e.Buffer.ToString().Should().Be("helXlo");
        e.Right(); e.End();
        e.Cursor.Should().Be(e.Buffer.Length);
    }

    [Fact]
    public void backspace_and_delete()
    {
        var e = new AppConfigCli.LineEditorEngine();
        e.SetInitial("abc");
        e.Left(); // ab|c
        e.Backspace(); // a|c
        e.Buffer.ToString().Should().Be("ac");
        e.Delete(); // a|
        e.Buffer.ToString().Should().Be("a");
    }

    [Fact]
    public void ctrl_word_navigation()
    {
        var e = new AppConfigCli.LineEditorEngine();
        e.SetInitial("foo-bar baz"); // punctuation splits words
        e.Home();
        e.CtrlWordRight(); // at start of foo -> after foo
        e.Buffer.ToString().Substring(e.Cursor).Should().Be("-bar baz");
        e.CtrlWordRight(); // jump over '-' and 'bar'
        e.Buffer.ToString().Substring(e.Cursor).Should().Be(" baz");
        e.CtrlWordLeft();
        e.Buffer.ToString().Substring(e.Cursor).Should().StartWith("bar baz".Substring(0, 3));
    }

    [Fact]
    public void ctrl_word_delete_and_backspace()
    {
        var e = new AppConfigCli.LineEditorEngine();
        e.SetInitial("foo bar baz");
        e.Home();
        e.CtrlWordRight(); // after foo
        e.CtrlWordDelete(); // delete space+bar
        e.Buffer.ToString().Should().Be("foo baz");
        e.End();
        e.CtrlWordBackspace(); // delete baz (leaves prior space, matching current editor behavior)
        e.Buffer.ToString().Should().Be("foo ");
    }

    [Fact]
    public void viewport_and_ellipses()
    {
        var e = new AppConfigCli.LineEditorEngine();
        e.SetInitial("abcdefghijklmnopqrstuvwxyz");
        e.End();
        var width = 10;
        e.EnsureVisible(width);
        var view = e.GetView(width);
        view.Should().StartWith("…");
        // At buffer end, trailing ellipsis is not shown
        view.EndsWith("…").Should().BeFalse();
        // Move to start and ensure leading ellipsis disappears
        e.Home();
        e.EnsureVisible(width);
        e.ScrollStart.Should().Be(0);
        e.GetView(width).StartsWith("…").Should().BeFalse();
    }
}
