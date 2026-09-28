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
        return ToolHomePath.Resolve(
            configuredHome,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ResolveZcodeHome(homeDirectory));
    }
}
