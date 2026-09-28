namespace TokenUsage.Providers.Claude;

public static class ClaudeConfigLocator
{
    public static IReadOnlyList<string> FindProjectDirectories(
        string homeDirectory,
        string? configDirectoryOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);

        IEnumerable<string> configuredRoots = string.IsNullOrWhiteSpace(configDirectoryOverride)
            ? [Path.Combine(homeDirectory, ".claude")]
            : configDirectoryOverride.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string configuredRoot in configuredRoots)
        {
            string expanded = ToolHomePath.ExpandHome(configuredRoot, homeDirectory);
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(expanded);
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
            {
                continue;
            }

            string projectsPath = string.Equals(
                Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
                "projects",
                StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : Path.Combine(fullPath, "projects");
            if (Directory.Exists(projectsPath) && seen.Add(projectsPath))
            {
                results.Add(projectsPath);
            }
        }

        return results;
    }
}
