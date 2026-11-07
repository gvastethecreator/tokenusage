using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

public static class LocalUsageCliAccess
{
    public static Task<bool> RequestRefreshAsync(string dataDirectory, TimeProvider clock,
        CancellationToken cancellationToken = default) =>
        CreateRefresh(dataDirectory, clock).TryRefreshAsync(cancellationToken);

    public static Task<LocalUsageRefreshResult> RefreshAsync(
        string dataDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken = default) =>
        CreateRefresh(dataDirectory, clock).RefreshAsync(cancellationToken);

    private static LocalUsageRefresh CreateRefresh(string dataDirectory, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        string fullDataDirectory = Path.GetFullPath(dataDirectory);
        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            fullDataDirectory,
            clock);
        return new LocalUsageRefresh(
            Path.Combine(fullDataDirectory, "scanner", "usage.v1.db"),
            composition.LocalUsageSources,
            clock);
    }
}
