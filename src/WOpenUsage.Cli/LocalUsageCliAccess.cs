using WOpenUsage.Core.Usage;

namespace WOpenUsage.Cli;

public sealed record UsageCliSummary(
    int EventCount,
    long TotalTokens,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    long UnpricedTokens);

public static class LocalUsageCliAccess
{
    public static async Task<UsageCliSummary> ReadAsync(
        string databasePath,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenAsync(
            databasePath,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            fromInclusive,
            toInclusive,
            cancellationToken).ConfigureAwait(false);

        int eventCount = 0;
        long totalTokens = 0;
        long unpricedTokens = 0;
        decimal reportedCostUsd = 0m;
        decimal estimatedCostUsd = 0m;
        bool hasReported = false;
        bool hasEstimated = false;
        checked
        {
            foreach (DailyUsageRollup rollup in rollups)
            {
                eventCount += rollup.EventCount;
                totalTokens += rollup.Tokens.Total;
                unpricedTokens += rollup.UnpricedTokens;
                if (rollup.ReportedCostUsd is decimal reported)
                {
                    reportedCostUsd += reported;
                    hasReported = true;
                }

                if (rollup.EstimatedCostUsd is decimal estimated)
                {
                    estimatedCostUsd += estimated;
                    hasEstimated = true;
                }
            }
        }

        return new UsageCliSummary(
            eventCount,
            totalTokens,
            hasReported ? reportedCostUsd : null,
            hasEstimated ? estimatedCostUsd : null,
            unpricedTokens);
    }
}
