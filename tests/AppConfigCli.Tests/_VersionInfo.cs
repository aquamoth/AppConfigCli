using System;
using Xunit;
using AppConfigCli;

namespace AppConfigCli.Tests;

public class _VersionInfo
{
    [Fact]
    public void version_line_includes_plus_commit()
    {
        var line = VersionInfo.GetVersionLine();
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("+", line);
    }
}
