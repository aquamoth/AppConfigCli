using FluentAssertions;
using Xunit;

public class CommandParserTests
{
    [Theory]
    [InlineData("add", typeof(AppConfigCli.Command.Add))]
    [InlineData("a", typeof(AppConfigCli.Command.Add))]
    [InlineData("save", typeof(AppConfigCli.Command.Save))]
    [InlineData("s", typeof(AppConfigCli.Command.Save))]
    [InlineData("reload", typeof(AppConfigCli.Command.Reload))]
    [InlineData("help", typeof(AppConfigCli.Command.Help))]
    [InlineData("q", typeof(AppConfigCli.Command.Quit))]
    public void Parses_simple_commands(string input, System.Type expected)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().NotBeNull();
        cmd!.GetType().Should().Be(expected);
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("edit 3", 3)]
    [InlineData("e 10", 10)]
    public void Parses_edit_with_index(string input, int idx)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().BeOfType<AppConfigCli.Command.Edit>();
        (cmd as AppConfigCli.Command.Edit)!.Index.Should().Be(idx);
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("delete 2 5", 2, 5)]
    [InlineData("d 7", 7, 7)]
    [InlineData("copy 1 3", 1, 3)]
    public void Parses_range_commands(string input, int s, int e)
    {
        AppConfigCli.CommandParser.TryParse(input, out var cmd, out var err).Should().BeTrue();
        cmd.Should().NotBeNull();
        switch (cmd)
        {
            case AppConfigCli.Command.Delete del:
                del.Start.Should().Be(s); del.End.Should().Be(e); break;
            case AppConfigCli.Command.Copy cp:
                cp.Start.Should().Be(s); cp.End.Should().Be(e); break;
            default:
                false.Should().BeTrue("Expected range command");
                break;
        }
        err.Should().BeNull();
    }

    [Fact]
    public void Parses_label_clear_and_empty()
    {
        AppConfigCli.CommandParser.TryParse("l", out var clear, out _).Should().BeTrue();
        (clear as AppConfigCli.Command.Label)!.Clear.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("label -", out var empty, out _).Should().BeTrue();
        var lbl = (empty as AppConfigCli.Command.Label)!;
        lbl.Empty.Should().BeTrue();
        lbl.Value.Should().Be("");
        AppConfigCli.CommandParser.TryParse("label dev", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Command.Label)!.Value.Should().Be("dev");
    }

    [Fact]
    public void Parses_grep_clear_and_value()
    {
        AppConfigCli.CommandParser.TryParse("g", out var clear, out _).Should().BeTrue();
        (clear as AppConfigCli.Command.Grep)!.Clear.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("grep ^C", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Command.Grep)!.Pattern.Should().Be("^C");
    }

    [Fact]
    public void Parses_prefix_prompt_and_value()
    {
        AppConfigCli.CommandParser.TryParse("p", out var prompt, out _).Should().BeTrue();
        (prompt as AppConfigCli.Command.Prefix)!.Prompt.Should().BeTrue();
        AppConfigCli.CommandParser.TryParse("prefix app:settings:", out var val, out _).Should().BeTrue();
        (val as AppConfigCli.Command.Prefix)!.Value.Should().Be("app:settings:");
    }

    [Fact]
    public void Parses_json_yaml_separators()
    {
        AppConfigCli.CommandParser.TryParse("json :", out var j, out _).Should().BeTrue();
        (j as AppConfigCli.Command.Json)!.Separator.Should().Be(":");
        AppConfigCli.CommandParser.TryParse("yaml .", out var y, out _).Should().BeTrue();
        (y as AppConfigCli.Command.Yaml)!.Separator.Should().Be(".");
    }

    [Fact]
    public void Json_Yaml_default_separator_is_colon()
    {
        AppConfigCli.CommandParser.TryParse("json", out var j0, out _).Should().BeTrue();
        (j0 as AppConfigCli.Command.Json)!.Separator.Should().Be(":");

        AppConfigCli.CommandParser.TryParse("yaml", out var y0, out _).Should().BeTrue();
        (y0 as AppConfigCli.Command.Yaml)!.Separator.Should().Be(":");
    }

    [Fact]
    public void Unknown_command_errors()
    {
        AppConfigCli.CommandParser.TryParse("zzz", out var cmd, out var err).Should().BeFalse();
        cmd.Should().BeNull();
        err.Should().NotBeNull();
    }
}
