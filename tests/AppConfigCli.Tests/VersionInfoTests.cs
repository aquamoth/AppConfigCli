using System;
using Xunit;
using AppConfigCli;

namespace AppConfigCli.Tests;

public class VersionInfoTests
{
    [Fact]
    public void VersionLine_IncludesCommit()
    {
        var line = VersionInfo.GetVersionLine();
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("commit", line, StringComparison.OrdinalIgnoreCase);
    }
}
