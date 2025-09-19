using System;
using Xunit;
using AppConfigCli;

namespace AppConfigCli.Tests;

public class VersionInfoTests
{
    [Fact]
    public void VersionLine_IncludesPlusCommit()
    {
        var line = VersionInfo.GetVersionLine();
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("+", line);
    }
}
