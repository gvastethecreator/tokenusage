using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Automation;

public sealed record UsageReportMetrics(
    int EventCount,
    TokenBreakdown Tokens,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    long UnpricedTokens,
    int UnavailableCostEventCount,
    CoverageKind Coverage)
{
    public decimal TotalCostUsd =>
        (ReportedCostUsd ?? 0m) + (EstimatedCostUsd ?? 0m);

    public decimal PriceCoveragePercent => Tokens.Total == 0
        ? 0m
        : decimal.Round(
            (Tokens.Total - UnpricedTokens) * 100m / Tokens.Total,
            1,
            MidpointRounding.AwayFromZero);
}

public sealed record UsageAgentReport(
    AgentId AgentId,
    UsageReportMetrics Metrics);

public sealed record UsageModelReport(
    AgentId AgentId,
    ModelProviderId? ModelProviderId,
    ModelId ModelId,
    UsageReportMetrics Metrics);

public sealed record UsageDayReport(
    DateOnly Date,
    UsageReportMetrics Metrics);

public sealed record UsageReport(
    UsageReportMetrics Totals,
    IReadOnlyList<UsageAgentReport> Agents,
    IReadOnlyList<UsageModelReport> Models,
    IReadOnlyList<UsageDayReport> Days);

public sealed class UsageReportQuery
{
    private readonly string _databasePath;

    public UsageReportQuery(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<UsageReport> ReadAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId = null,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DailyUsageRollup> rollups = agentId is null
            ? await repository.QueryDailyRollupsAsync(
                fromInclusive,
                toInclusive,
                cancellationToken).ConfigureAwait(false)
            : await repository.QueryDailyRollupsByAgentAsync(
                fromInclusive,
                toInclusive,
                agentId,
                cancellationToken).ConfigureAwait(false);

        return Build(rollups);
    }

    public static UsageReport Build(IEnumerable<DailyUsageRollup> rollups)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        DailyUsageRollup[] snapshot = rollups.ToArray();

        UsageAgentReport[] agents = snapshot
            .GroupBy(rollup => rollup.AgentId)
            .Select(group => new UsageAgentReport(group.Key, Aggregate(group)))
            .OrderByDescending(item => item.Metrics.TotalCostUsd)
            .ThenByDescending(item => item.Metrics.Tokens.Total)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ToArray();

        UsageModelReport[] models = snapshot
            .GroupBy(rollup => new
            {
                rollup.AgentId,
                rollup.ModelProviderId,
                rollup.ModelId,
            })
            .Select(group => new UsageModelReport(
                group.Key.AgentId,
                group.Key.ModelProviderId,
                group.Key.ModelId,
                Aggregate(group)))
            .OrderByDescending(item => item.Metrics.TotalCostUsd)
            .ThenByDescending(item => item.Metrics.Tokens.Total)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ModelId.Value, StringComparer.Ordinal)
            .ToArray();

        UsageDayReport[] days = snapshot
            .GroupBy(rollup => rollup.Date)
            .Select(group => new UsageDayReport(group.Key, Aggregate(group)))
            .OrderBy(item => item.Date)
            .ToArray();

        return new UsageReport(Aggregate(snapshot), agents, models, days);
    }

    private static UsageReportMetrics Aggregate(IEnumerable<DailyUsageRollup> rollups)
    {
        int eventCount = 0;
        long input = 0;
        long output = 0;
        long reasoning = 0;
        long cacheRead = 0;
        long cacheWrite = 0;
        decimal reportedCostUsd = 0m;
        decimal estimatedCostUsd = 0m;
        bool hasReportedCost = false;
        bool hasEstimatedCost = false;
        long unpricedTokens = 0;
        int unavailableCostEventCount = 0;
        CoverageKind coverage = CoverageKind.Complete;

        checked
        {
            foreach (DailyUsageRollup rollup in rollups)
            {
                eventCount += rollup.EventCount;
                input += rollup.Tokens.Input;
                output += rollup.Tokens.Output;
                reasoning += rollup.Tokens.Reasoning;
                cacheRead += rollup.Tokens.CacheRead;
                cacheWrite += rollup.Tokens.CacheWrite;
                unpricedTokens += rollup.UnpricedTokens;
                unavailableCostEventCount += rollup.UnavailableCostEventCount;

                if (rollup.ReportedCostUsd is decimal reported)
                {
                    reportedCostUsd += reported;
                    hasReportedCost = true;
                }

                if (rollup.EstimatedCostUsd is decimal estimated)
                {
                    estimatedCostUsd += estimated;
                    hasEstimatedCost = true;
                }

                if (CoverageRank(rollup.Coverage) > CoverageRank(coverage))
                {
                    coverage = rollup.Coverage;
                }
            }
        }

        return new UsageReportMetrics(
            eventCount,
            new TokenBreakdown(input, output, reasoning, cacheRead, cacheWrite),
            hasReportedCost ? reportedCostUsd : null,
            hasEstimatedCost ? estimatedCostUsd : null,
            unpricedTokens,
            unavailableCostEventCount,
            coverage);
    }

    private static int CoverageRank(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => 0,
        CoverageKind.Partial => 1,
        CoverageKind.SummaryOnly => 2,
        CoverageKind.Unpriced => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };
}
