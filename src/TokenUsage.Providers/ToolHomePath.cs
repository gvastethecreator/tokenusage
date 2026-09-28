namespace TokenUsage.Providers;

/// <summary>
/// Resolves a tool's home folder the way the tools themselves read it: an explicit value
/// (for example CODEX_HOME or GROK_HOME) with "~" standing for the user profile, or the default
/// folder. Readers and hook installers share it so they always look at the same place.
/// </summary>
public static class ToolHomePath
{
    public static string ExpandHome(string value, string userHome)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(userHome);
        if (value == "~")
        {
            return userHome;
        }

        return value.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || value.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            ? Path.Combine(userHome, value[2..])
            : value;
    }

    /// <summary>
    /// The configured folder, or <paramref name="defaultHome"/> when none is set. A value that
    /// cannot become a path falls back to the default instead of failing the scan.
    /// </summary>
    public static string Resolve(string? configured, string userHome, string defaultHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultHome);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(defaultHome);
        }

        try
        {
            return Path.GetFullPath(ExpandHome(configured.Trim(), userHome));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return Path.GetFullPath(defaultHome);
        }
    }
}
