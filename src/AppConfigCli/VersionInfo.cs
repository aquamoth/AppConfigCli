namespace AppConfigCli;

public static class VersionInfo
{
    public static string GetVersionLine()
    {
        var name = "AppConfigCli";
        string version = ThisAssembly.AssemblyInformationalVersion;
        var baseVersion = version?.Split('+')[0] ?? version ?? "0.0";
        string shortCommit = "unknown";

        // Prefer NBGV-provided commit id
        try
        {
            if (!string.IsNullOrWhiteSpace(ThisAssembly.GitCommitId))
            {
                var full = ThisAssembly.GitCommitId;
                shortCommit = full.Length >= 7 ? full.Substring(0, 7) : full;
            }
        }
        catch { }

        // Fallback: parse from informational version (+g<sha> or +<sha>)
        if (shortCommit == "unknown" && !string.IsNullOrEmpty(version))
        {
            var plusIdx = version.IndexOf('+');
            if (plusIdx >= 0)
            {
                var start = plusIdx + 1;
                if (start < version.Length && version[start] == 'g') start++;
                int len = 0;
                while (start + len < version.Length && char.IsLetterOrDigit(version[start + len])) len++;
                if (len > 0) shortCommit = version.Substring(start, Math.Min(7, len));
            }
        }

        // Branch labeling: include sanitized branch for non-main
        var branch = GetBranchNameOrNull();
        string label = (!string.IsNullOrEmpty(branch) && !IsMain(branch)) ? ("-" + SanitizeBranch(branch)) : string.Empty;
        return $"{name} v{baseVersion}{label}+{shortCommit}";
    }

    private static bool IsMain(string branch)
        => string.Equals(branch, "main", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(branch, "master", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeBranch(string branch)
    {
        var sb = new System.Text.StringBuilder(branch.Length);
        foreach (var ch in branch)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s;
    }

    private static string? GetBranchNameOrNull()
    {
        string[] envVars = new[]
        {
            "GITHUB_REF_NAME",
            "BUILD_SOURCEBRANCHNAME",
            "CI_COMMIT_REF_NAME",
            "BRANCH_NAME",
            "GIT_BRANCH"
        };
        foreach (var v in envVars)
        {
            var val = System.Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        try
        {
            var cwd = System.Environment.CurrentDirectory;
            if (System.IO.Directory.Exists(System.IO.Path.Combine(cwd, ".git")))
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "git";
                p.StartInfo.Arguments = "rev-parse --abbrev-ref HEAD";
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                if (!p.WaitForExit(300)) { try { p.Kill(); } catch { } return null; }
                var stdout = p.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrWhiteSpace(stdout) && stdout != "HEAD") return stdout;
            }
        }
        catch { }
        return null;
    }
}
