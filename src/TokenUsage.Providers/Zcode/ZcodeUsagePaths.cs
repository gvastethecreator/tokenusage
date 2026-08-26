namespace TokenUsage.Providers.Zcode;

public static class ZcodeUsagePaths
{
    public const string DatabaseFileName = "db.sqlite";

    public static string ResolveZcodeHome(string? homeDirectory = null)
    {
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(Path.GetFullPath(home), ".zcode");
    }

    public static string ResolveDatabasePath(string zcodeHome) =>
        Path.Combine(Path.GetFullPath(zcodeHome), "cli", "db", DatabaseFileName);

    /// <summary>
    /// Applies an explicit home override, the ZCODE_HOME variable shape, or the
    /// user profile default. A value that cannot become a path falls back to
    /// the default instead of failing the scan.
    /// </summary>
    public static string ResolveConfiguredHome(
        string? homeDirectory,
        string? configuredHome)
    {
        string defaultHome = ResolveZcodeHome(homeDirectory);
        if (string.IsNullOrWhiteSpace(configuredHome))
        {
            return defaultHome;
        }

        string raw = configuredHome.Trim();
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (raw == "~")
        {
            raw = userHome;
        }
        else if (raw.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 || raw.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            raw = Path.Combine(userHome, raw[2..]);
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return defaultHome;
        }
    }
}
