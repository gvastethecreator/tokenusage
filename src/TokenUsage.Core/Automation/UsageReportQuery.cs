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

    public decimal PriceCoveragePercent
    {
        get
        {
            if (Tokens.Total == 0)
            {
                return 0m;
            }

            decimal percent = decimal.Round(
                (Tokens.Total - UnpricedTokens) * 100m / Tokens.Total,
                1,
                MidpointRounding.AwayFromZero);
            return UnpricedTokens > 0 && UnpricedTokens < Tokens.Total
                ? Math.Min(percent, 99.9m)
                : percent;
        }
    }
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

public sealed record UsageAgentDayReport(
    DateOnly Date,
    AgentId AgentId,
    UsageReportMetrics Metrics);

public sealed record UsageModelDayReport(
    DateOnly Date,
    AgentId AgentId,
    ModelProviderId? ModelProviderId,
    ModelId ModelId,
    UsageReportMetrics Metrics);

public sealed record UsageReport(
    UsageReportMetrics Totals,
    IReadOnlyList<UsageAgentReport> Agents,
    IReadOnlyList<UsageModelReport> Models,
    IReadOnlyList<UsageDayReport> Days,
    IReadOnlyList<UsageAgentDayReport> AgentDays,
    IReadOnlyList<UsageModelDayReport> ModelDays);

public sealed record UsageReportMetricDelta(
    int EventCount,
    long Tokens,
    decimal TotalCostUsd,
    decimal ReportedCostUsd,
    decimal EstimatedCostUsd,
    long UnpricedTokens);

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

    public async Task<(DateOnly From, DateOnly To)?> ReadAvailableDateRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        return await repository.QueryDailyRollupRangeAsync(
            fromInclusive,
            toInclusive,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads events in a UTC half-open range. Reset-cycle reports use this path so activity on
    /// a reset date stays in the cycle that contains its event timestamp.
    /// </summary>
    public async Task<UsageReport> ReadExactAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId? agentId = null,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UsageEvent> events = await repository.QueryUsageEventsAsync(
            fromInclusiveUtc,
            toExclusiveUtc,
            agentId,
            cancellationToken).ConfigureAwait(false);
        return Build(UsageRollupAggregator.Aggregate(events));
    }

    /// <summary>
    /// Same agent slice as a second repository read for that agent, taken from a report
    /// that already covers every agent in the range.
    /// </summary>
    public static UsageReport FilterByAgent(UsageReport report, AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(agentId);

        UsageAgentReport[] agents = report.Agents
            .Where(item => item.AgentId == agentId)
            .ToArray();
        UsageModelReport[] models = report.Models
            .Where(item => item.AgentId == agentId)
            .ToArray();
        UsageAgentDayReport[] agentDays = report.AgentDays
            .Where(item => item.AgentId == agentId)
            .ToArray();
        UsageDayReport[] days = agentDays
            .Select(item => new UsageDayReport(item.Date, item.Metrics))
            .OrderBy(item => item.Date)
            .ToArray();
        return new UsageReport(
            agents.Length == 0 ? Build([]).Totals : agents[0].Metrics,
            agents,
            models,
            days,
            agentDays,
            report.ModelDays.Where(item => item.AgentId == agentId).ToArray());
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

        UsageAgentDayReport[] agentDays = snapshot
            .GroupBy(rollup => new
            {
                rollup.Date,
                rollup.AgentId,
            })
            .Select(group => new UsageAgentDayReport(
                group.Key.Date,
                group.Key.AgentId,
                Aggregate(group)))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ToArray();

        UsageModelDayReport[] modelDays = snapshot
            .GroupBy(rollup => new
            {
                rollup.Date,
                rollup.AgentId,
                rollup.ModelProviderId,
                rollup.ModelId,
            })
            .Select(group => new UsageModelDayReport(
                group.Key.Date, group.Key.AgentId, group.Key.ModelProviderId,
                group.Key.ModelId, Aggregate(group)))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ModelId.Value, StringComparer.Ordinal)
            .ToArray();
        return new UsageReport(Aggregate(snapshot), agents, models, days, agentDays, modelDays);
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

    public static UsageReportMetricDelta Subtract(
        UsageReportMetrics current,
        UsageReportMetrics baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        return new UsageReportMetricDelta(
            current.EventCount - baseline.EventCount,
            current.Tokens.Total - baseline.Tokens.Total,
            current.TotalCostUsd - baseline.TotalCostUsd,
            (current.ReportedCostUsd ?? 0m) - (baseline.ReportedCostUsd ?? 0m),
            (current.EstimatedCostUsd ?? 0m) - (baseline.EstimatedCostUsd ?? 0m),
            current.UnpricedTokens - baseline.UnpricedTokens);
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
