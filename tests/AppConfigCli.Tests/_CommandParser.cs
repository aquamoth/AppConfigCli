using FluentAssertions;
using Xunit;

public class _CommandParser
{
    [Theory]
    [InlineData("add", typeof(AppConfigCli.Editor.Commands.Add))]
    [InlineData("a", typeof(AppConfigCli.Editor.Commands.Add))]
    [InlineData("save", typeof(AppConfigCli.Editor.Commands.Save))]
    [InlineData("s", typeof(AppConfigCli.Editor.Commands.Save))]
    [InlineData("reload", typeof(AppConfigCli.Editor.Commands.Reload))]
    [InlineData("help", typeof(AppConfigCli.Editor.Commands.Help))]
    [InlineData("q", typeof(AppConfigCli.Editor.Commands.Quit))]
    public void parses_simple_commands(string input, System.Type expected)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().NotBeNull();
        cmd!.GetType().Should().Be(expected);
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("10", 10)]
    public void parses_numeric_edit_with_index(string input, int idx)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().BeOfType<AppConfigCli.Editor.Commands.Edit>();
        (cmd as AppConfigCli.Editor.Commands.Edit)!.Index.Should().Be(idx);
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("delete 2 5", 2, 5)]
    [InlineData("d 7", 7, 7)]
    [InlineData("copy 1 3", 1, 3)]
    public void parses_range_commands(string input, int s, int e)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().NotBeNull();
        switch (cmd)
        {
            case AppConfigCli.Editor.Commands.Delete del:
                del.Start.Should().Be(s); del.End.Should().Be(e); break;
            case AppConfigCli.Editor.Commands.Copy cp:
                cp.Start.Should().Be(s); cp.End.Should().Be(e); break;
            default:
                false.Should().BeTrue("Expected range command");
                break;
        }
        err.Should().BeNull();
    }

    [Fact]
    public void parses_label_clear_and_empty()
    {
        AppConfigCli.CommandParser.TryParse("l", out var clear, out _).Should().BeTrue();
        (clear as AppConfigCli.Editor.Commands.Label)!.Clear.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("label -", out var empty, out _).Should().BeTrue();
        var lbl = (empty as AppConfigCli.Editor.Commands.Label)!;
        lbl.Empty.Should().BeTrue();
        lbl.Value.Should().Be("");
        AppConfigCli.CommandParser.TryParse("label dev", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Editor.Commands.Label)!.Value.Should().Be("dev");
    }

    [Fact]
    public void parses_grep_clear_and_value()
    {
        AppConfigCli.CommandParser.TryParse("g", out var clear, out _).Should().BeTrue();
        (clear as AppConfigCli.Editor.Commands.Grep)!.Clear.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("grep ^C", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Editor.Commands.Grep)!.Pattern.Should().Be("^C");
    }

    [Fact]
    public void parses_prefix_prompt_and_value()
    {
        AppConfigCli.CommandParser.TryParse("p", out var prompt, out _).Should().BeTrue();
        (prompt as AppConfigCli.Editor.Commands.Prefix)!.Prompt.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("prefix app:settings:", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Editor.Commands.Prefix)!.Value.Should().Be("app:settings:");
    }

    [Fact]
    public void parses_json_yaml_separators()
    {
        AppConfigCli.CommandParser.TryParse("json :", out var j, out _).Should().BeTrue();
        (j as AppConfigCli.Editor.Commands.Json)!.Separator.Should().Be(":");
        AppConfigCli.CommandParser.TryParse("yaml .", out var y, out _).Should().BeTrue();
        (y as AppConfigCli.Editor.Commands.Yaml)!.Separator.Should().Be(".");
    }

    [Fact]
    public void uses_color_separator_by_default_for_Json_and_Yaml()
    {
        AppConfigCli.CommandParser.TryParse("json", out var j0, out _).Should().BeTrue();
        (j0 as AppConfigCli.Editor.Commands.Json)!.Separator.Should().Be(":");

        AppConfigCli.CommandParser.TryParse("yaml", out var y0, out _).Should().BeTrue();
        (y0 as AppConfigCli.Editor.Commands.Yaml)!.Separator.Should().Be(":");
    }

    [Fact]
    public void outputs_error_for_unknown_commands()
    {
        AppConfigCli.CommandParser.TryParse("zzz", out var cmd, out var err).Should().BeFalse();
        cmd.Should().BeNull();
        err.Should().NotBeNull();
    }

    [Fact]
    public void parses_replace_command()
    {
        AppConfigCli.CommandParser.TryParse("replace", out var cmd, out var err).Should().BeTrue();
        cmd.Should().NotBeNull();
        cmd!.GetType().Should().Be(typeof(AppConfigCli.Editor.Commands.Replace));
        err.Should().BeNull();
    }
}
