namespace TokenUsage.Providers.Cursor;

public static class CursorUsagePaths
{
    public static string ResolveCursorHome(string? homeDirectory = null)
    {
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(Path.GetFullPath(home), ".cursor");
    }

    public static string ResolveStateDatabasePath(string? roamingAppDataDirectory = null)
    {
        string roamingAppData = roamingAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(
            Path.GetFullPath(roamingAppData),
            "Cursor",
            "User",
            "globalStorage",
            "state.vscdb");
    }
}
