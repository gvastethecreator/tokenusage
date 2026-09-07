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

public sealed record UsageModelProfile(string AgentId, string CanonicalModel, string? ObservedModel,
    string? Effort, string? Tier, DateTimeOffset FirstObservedAtUtc, DateTimeOffset LastObservedAtUtc, long Tokens);

public sealed record UsageElapsedBucket(int Index, AgentId AgentId, UsageReportMetrics Metrics);

public sealed record UsageReport(
    UsageReportMetrics Totals,
    IReadOnlyList<UsageAgentReport> Agents,
    IReadOnlyList<UsageModelReport> Models,
    IReadOnlyList<UsageDayReport> Days,
    IReadOnlyList<UsageAgentDayReport> AgentDays,
    IReadOnlyList<UsageModelDayReport> ModelDays)
{
    public IReadOnlyList<UsageTimeRollup> TimeBuckets { get; init; } = [];

    public IReadOnlyList<UsageElapsedBucket> ElapsedTwoHourBuckets { get; init; } = [];

    public IReadOnlyList<AccountUsageAggregate> AccountUsage { get; init; } = [];

    public IReadOnlyList<string> PricingVersions { get; init; } = [];

    public IReadOnlyList<string> ParserVersions { get; init; } = [];

    public int ExcludedTimingRecords { get; init; }

    public bool IsExactInterval { get; init; }

    public bool HasTimingGaps { get; init; }

    public IReadOnlyList<string> GroupingTimeZoneIds { get; init; } = [];

    public long PriceReferenceExcludedTokens { get; init; }

    public IReadOnlyList<UsageModelProfile> ModelProfiles { get; init; } = [];

    public long? PopulationTokens { get; init; }

    public IReadOnlyList<UsageCollectionState> CollectionState { get; init; } = [];
}

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
        bool includeTimeBuckets = false,
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

        var versions = await repository.ReadReportVersionsAsync(fromInclusive, toInclusive, agentId, cancellationToken).ConfigureAwait(false);
        UsageReport report = Build(rollups) with
        {
            PricingVersions = versions.Pricing,
            ParserVersions = versions.Parsers,
            AccountUsage = await repository.ReadAccountUsageAsync(fromInclusive, toInclusive, agentId, cancellationToken).ConfigureAwait(false),
            CollectionState = (await repository.ReadCollectionStateAsync(cancellationToken).ConfigureAwait(false)).Where(row => agentId is null || row.AgentId == agentId.Value).ToArray(),
        };
        if (!includeTimeBuckets) return report;
        IReadOnlyList<UsageTimeRollup> buckets = await repository.QueryTwoHourRollupsAsync(
            fromInclusive, toInclusive, agentId, cancellationToken).ConfigureAwait(false);
        int excluded = Math.Max(0, report.Totals.EventCount - buckets.Sum(item => item.Usage.EventCount));
        return report with { TimeBuckets = buckets, HasTimingGaps = excluded > 0, ExcludedTimingRecords = excluded };
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
        if (toExclusiveUtc <= fromInclusiveUtc) throw new ArgumentException("The exact interval must have positive duration.", nameof(toExclusiveUtc));
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UsageEvent> candidates = await repository.QueryUsageEventsAsync(
            fromInclusiveUtc.AddDays(-1),
            toExclusiveUtc.AddDays(1),
            agentId,
            includeOverlappingIntervals: true, cancellationToken).ConfigureAwait(false);
        UsageEvent[] events = candidates.Where(item =>
            item.OccurredAtUtc >= fromInclusiveUtc && item.OccurredAtUtc < toExclusiveUtc
            && (item.TimePrecision == UsageTimePrecision.Timestamp
                || item.TimePrecision == UsageTimePrecision.Interval && item.IntervalStartedAtUtc >= fromInclusiveUtc)).ToArray();
        var acceptedKeys = events.Select(item => item.EventKey).ToHashSet();
        int excluded = candidates.Count(item =>
        {
            if (acceptedKeys.Contains(item.EventKey) || item.TimePrecision == UsageTimePrecision.Timestamp) return false;
            if (item.TimePrecision == UsageTimePrecision.Interval)
                return item.IntervalStartedAtUtc < toExclusiveUtc && item.OccurredAtUtc >= fromInclusiveUtc;
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId);
            DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAtUtc, zone).DateTime);
            return date >= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusiveUtc, zone).DateTime)
                && date <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toExclusiveUtc.AddTicks(-1), zone).DateTime);
        });
        DateOnly firstDate = DateOnly.FromDateTime(fromInclusiveUtc.UtcDateTime.AddDays(-1));
        DateOnly lastDate = DateOnly.FromDateTime(toExclusiveUtc.UtcDateTime.AddDays(1));
        IReadOnlyList<DailyUsageRollup> retained = agentId is null
            ? await repository.QueryDailyRollupsAsync(firstDate, lastDate, cancellationToken).ConfigureAwait(false)
            : await repository.QueryDailyRollupsByAgentAsync(firstDate, lastDate, agentId, cancellationToken).ConfigureAwait(false);
        // Daily-only retained history cannot establish an exact interval, even when raw events are gone.
        var supportedCounts = candidates.Where(item => item.TimePrecision is UsageTimePrecision.Timestamp or UsageTimePrecision.Interval)
            .GroupBy(item => (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId)).DateTime),
                item.GroupingTimeZoneId, item.AgentId, item.ModelProviderId, item.ModelId))
            .ToDictionary(group => group.Key, group => group.Count());
        bool timingGaps = retained.Any(row =>
        {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(row.GroupingTimeZoneId);
            DateOnly from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusiveUtc, zone).DateTime);
            DateOnly to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toExclusiveUtc.AddTicks(-1), zone).DateTime);
            return row.Date >= from && row.Date <= to && row.EventCount > supportedCounts.GetValueOrDefault(
                (row.Date, row.GroupingTimeZoneId, row.AgentId, row.ModelProviderId, row.ModelId));
        });
        return Build(UsageRollupAggregator.Aggregate(events)) with
        {
            ExcludedTimingRecords = excluded,
            PricingVersions = events.Select(item => item.Cost.CatalogVersion).OfType<string>().Distinct().Order().ToArray(),
            ParserVersions = events.Select(item => item.ParserVersion).Distinct().Order().ToArray(),
            HasTimingGaps = timingGaps || excluded > 0,
            CollectionState = (await repository.ReadCollectionStateAsync(cancellationToken).ConfigureAwait(false)).Where(row => agentId is null || row.AgentId == agentId.Value).ToArray(),
            IsExactInterval = true,
            ElapsedTwoHourBuckets = events.Where(item => item.TimePrecision == UsageTimePrecision.Timestamp)
                .GroupBy(item => (Index: (int)((item.OccurredAtUtc - fromInclusiveUtc).Ticks / TimeSpan.FromHours(2).Ticks), item.AgentId))
                .Select(group => new UsageElapsedBucket(group.Key.Index, group.Key.AgentId,
                    Aggregate(UsageRollupAggregator.Aggregate(group)))).ToArray(),
            TimeBuckets = events.Where(item => item.TimePrecision == UsageTimePrecision.Timestamp).GroupBy(item =>
            {
                DateTime local = TimeZoneInfo.ConvertTime(item.OccurredAtUtc,
                    TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId)).DateTime;
                return (DateOnly.FromDateTime(local), Hour: local.Hour / 2 * 2);
            }).SelectMany(group => UsageRollupAggregator.Aggregate(group)
                .Select(rollup => new UsageTimeRollup(rollup, group.Key.Hour))).ToArray(),
        };
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
            report.ModelDays.Where(item => item.AgentId == agentId).ToArray())
        {
            TimeBuckets = report.TimeBuckets.Where(item => item.Usage.AgentId == agentId).ToArray(),
            ElapsedTwoHourBuckets = report.ElapsedTwoHourBuckets.Where(item => item.AgentId == agentId).ToArray(),
            AccountUsage = report.AccountUsage.Where(item => item.AgentId == agentId).ToArray(),
            ExcludedTimingRecords = report.ExcludedTimingRecords,
            IsExactInterval = report.IsExactInterval,
            HasTimingGaps = report.HasTimingGaps,
            CollectionState = report.CollectionState.Where(row => row.AgentId == agentId.Value).ToArray(),
            PricingVersions = report.PricingVersions,
            ParserVersions = report.ParserVersions,
            GroupingTimeZoneIds = report.GroupingTimeZoneIds,
            PriceReferenceExcludedTokens = report.PriceReferenceExcludedTokens,
        };
    }

    public static UsageReport FilterByModel(UsageReport report, AgentId agentId, ModelId modelId) =>
        Build(report.ModelDays.Where(row => row.AgentId == agentId && row.ModelId == modelId)
            .Select(row => ToRollup(row, report.GroupingTimeZoneIds.Count == 1 ? report.GroupingTimeZoneIds[0] : "mixed-or-unknown"))) with
        {
            TimeBuckets = report.TimeBuckets.Where(row => row.Usage.AgentId == agentId && row.Usage.ModelId == modelId).ToArray(),
            PopulationTokens = report.Totals.Tokens.Total,
            ModelProfiles = report.ModelProfiles.Where(row => row.AgentId == agentId.Value && row.CanonicalModel == modelId.Value).ToArray(),
            ExcludedTimingRecords = report.ExcludedTimingRecords,
            IsExactInterval = report.IsExactInterval,
            HasTimingGaps = report.HasTimingGaps,
            CollectionState = report.CollectionState.Where(row => row.AgentId == agentId.Value).ToArray(),
            PricingVersions = report.PricingVersions,
            ParserVersions = report.ParserVersions,
            GroupingTimeZoneIds = report.GroupingTimeZoneIds,
            PriceReferenceExcludedTokens = report.PriceReferenceExcludedTokens,
        };

    internal static DailyUsageRollup ToRollup(UsageModelDayReport row, string timeZoneId) => new(row.Date,
        timeZoneId, row.AgentId, row.ModelProviderId, row.ModelId, row.Metrics.Tokens,
        row.Metrics.ReportedCostUsd, row.Metrics.EstimatedCostUsd, row.Metrics.UnpricedTokens,
        row.Metrics.UnavailableCostEventCount, row.Metrics.EventCount, row.Metrics.Coverage);

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
        return new UsageReport(Aggregate(snapshot), agents, models, days, agentDays, modelDays)
        {
            GroupingTimeZoneIds = snapshot.Select(row => row.GroupingTimeZoneId).Distinct().Order().ToArray(),
        };
    }

    public static UsageReportMetrics Aggregate(IEnumerable<DailyUsageRollup> rollups)
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
