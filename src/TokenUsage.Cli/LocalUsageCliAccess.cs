using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

public static class LocalUsageCliAccess
{
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
