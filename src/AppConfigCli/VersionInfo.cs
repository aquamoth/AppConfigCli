namespace AppConfigCli;

public static class VersionInfo
{
    public static string GetVersionLine()
    {
        var name = "AppConfigCli";
        string version = ThisAssembly.AssemblyInformationalVersion;
        var baseVersion = version?.Split('+')[0] ?? version ?? "0.0";
        string shortCommit = "unknown";
        // Attempt to parse "+g<sha>" from informational version (NBGV default)
        if (!string.IsNullOrEmpty(version))
        {
            var plusIdx = version.IndexOf('+');
            if (plusIdx >= 0 && plusIdx + 2 < version.Length && version[plusIdx + 1] == 'g')
            {
                var start = plusIdx + 2;
                int len = 0;
                while (start + len < version.Length && char.IsLetterOrDigit(version[start + len])) len++;
                if (len > 0)
                {
                    shortCommit = version.Substring(start, Math.Min(7, len));
                }
            }
        }
        return $"{name} v{baseVersion} (commit {shortCommit})";
    }
}
