namespace TokenUsage.Platform.Windows.Storage;

public static class TokenUsageDataDirectory
{
    public const string EnvironmentVariableName = "TOKENUSAGE_DATA_DIR";
    public const string PortableMarkerFileName = "TokenUsage.portable";
    public const string PortableDataDirectoryName = "Data";

    public static string Resolve(
        Func<string> packagedDataDirectory,
        string? applicationBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(packagedDataDirectory);

        return Resolve(
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            applicationBaseDirectory ?? AppContext.BaseDirectory,
            packagedDataDirectory);
    }

    internal static string Resolve(
        string? configuredDataDirectory,
        string applicationBaseDirectory,
        Func<string> packagedDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentNullException.ThrowIfNull(packagedDataDirectory);

        if (configuredDataDirectory is not null)
        {
            if (string.IsNullOrWhiteSpace(configuredDataDirectory))
            {
                throw new InvalidOperationException(
                    $"{EnvironmentVariableName} cannot be empty.");
            }

            return Path.GetFullPath(configuredDataDirectory);
        }

        string baseDirectory = Path.GetFullPath(applicationBaseDirectory);
        string? portableRoot = FindPortableRoot(baseDirectory);
        if (portableRoot is not null)
        {
            return Path.Combine(portableRoot, PortableDataDirectoryName);
        }

        return Path.GetFullPath(packagedDataDirectory());
    }

    private static string? FindPortableRoot(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        for (int depth = 0; depth < 2 && directory is not null; depth++)
        {
            if (File.Exists(Path.Combine(directory.FullName, PortableMarkerFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
