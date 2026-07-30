using WOpenUsage.Core.Usage;

namespace WOpenUsage.Core.Automation;

public sealed record UsageSummary(
    int EventCount,
    long TotalTokens,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    long UnpricedTokens);

public sealed class UsageQuery
{
    private readonly string _databasePath;

    public UsageQuery(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<UsageSummary> ReadAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            fromInclusive,
            toInclusive,
            cancellationToken).ConfigureAwait(false);
        return Summarize(rollups);
    }

    public static UsageSummary FromRefreshResult(LocalUsageRefreshResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Summarize(result.Rollups);
    }

    public static UsageSummary Summarize(IEnumerable<DailyUsageRollup> rollups)
    {
        ArgumentNullException.ThrowIfNull(rollups);

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

        return new UsageSummary(
            eventCount,
            totalTokens,
            hasReported ? reportedCostUsd : null,
            hasEstimated ? estimatedCostUsd : null,
            unpricedTokens);
    }
}
