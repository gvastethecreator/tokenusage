namespace TokenUsage.Providers.CopilotVsCode;

/// <summary>
/// Chat session locations of the VS Code builds that ship GitHub Copilot Chat.
/// Only the session files are listed. Workspace descriptors, extension storage,
/// logs, and editor settings are never enumerated.
/// </summary>
public static class CopilotVsCodeUsagePaths
{
    private static readonly string[] ProductDirectories = ["Code", "Code - Insiders", "VSCodium"];

    /// <summary>The <c>User</c> directory of each supported VS Code build.</summary>
    public static IReadOnlyList<string> ResolveUserDirectories(string? roamingAppDataDirectory = null)
    {
        string roamingAppData = Path.GetFullPath(roamingAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        return Array.AsReadOnly(ProductDirectories
            .Select(product => Path.Combine(roamingAppData, product, "User"))
            .ToArray());
    }

    /// <summary>
    /// Session files below one <c>User</c> directory:
    /// <c>workspaceStorage\*\chatSessions\*.jsonl</c> (operation log), the legacy
    /// <c>*.json</c> snapshots next to them, and <c>globalStorage\emptyWindowChatSessions\*</c>.
    /// Linked directories are not followed.
    /// </summary>
    public static IEnumerable<string> EnumerateSessionFiles(string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        string workspaceStorage = Path.Combine(userDirectory, "workspaceStorage");
        if (IsPlainDirectory(workspaceStorage))
        {
            foreach (string workspace in Directory.EnumerateDirectories(workspaceStorage))
            {
                string sessions = Path.Combine(workspace, "chatSessions");
                if (IsPlainDirectory(workspace) && IsPlainDirectory(sessions))
                {
                    foreach (string file in EnumerateSessionFilesIn(sessions))
                    {
                        yield return file;
                    }
                }
            }
        }

        string emptyWindow = Path.Combine(userDirectory, "globalStorage", "emptyWindowChatSessions");
        if (IsPlainDirectory(emptyWindow))
        {
            foreach (string file in EnumerateSessionFilesIn(emptyWindow))
            {
                yield return file;
            }
        }
    }

    internal static bool IsOperationLog(string path) =>
        path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateSessionFilesIn(string directory) =>
        Directory.EnumerateFiles(directory, "*.jsonl")
            .Concat(Directory.EnumerateFiles(directory, "*.json")
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));

    private static bool IsPlainDirectory(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
