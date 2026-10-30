using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

public static class LocalUsageCliAccess
{
    public static async Task<LocalUsageRefreshResult> RefreshAsync(
        string dataDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        string fullDataDirectory = Path.GetFullPath(dataDirectory);
        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            fullDataDirectory,
            clock);
        var refresh = new LocalUsageRefresh(
            Path.Combine(fullDataDirectory, "scanner", "usage.v1.db"),
            composition.LocalUsageSources,
            clock);
        return await refresh.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
