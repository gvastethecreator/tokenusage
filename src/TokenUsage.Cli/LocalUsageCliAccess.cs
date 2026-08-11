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

    public static async Task<UsageCliSummary> ReadAsync(
        string databasePath,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        return await new UsageQuery(databasePath)
            .ReadAsync(fromInclusive, toInclusive, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Formats a domain local-usage refresh result into the CLI summary contract.
    /// </summary>
    public static UsageCliSummary FromRefreshResult(LocalUsageRefreshResult result)
    {
        return UsageQuery.FromRefreshResult(result);
    }

    public static UsageCliSummary Summarize(IEnumerable<DailyUsageRollup> rollups)
    {
        return UsageQuery.Summarize(rollups);
    }
}
