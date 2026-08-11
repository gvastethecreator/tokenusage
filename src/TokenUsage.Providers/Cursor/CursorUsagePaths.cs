namespace TokenUsage.Providers.Cursor;

public static class CursorUsagePaths
{
    public const string SpoolFileName = "usage.v1.jsonl";

    public static string ResolveCursorHome(string? homeDirectory = null)
    {
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(Path.GetFullPath(home), ".cursor");
    }

    public static string ResolveSpoolPath(string? localAppDataDirectory = null)
    {
        string localAppData = localAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            Path.GetFullPath(localAppData),
            "TokenUsage",
            "cursor",
            SpoolFileName);
    }
}
