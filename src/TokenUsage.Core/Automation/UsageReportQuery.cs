using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Automation;

internal sealed record UsageExactTiming(AgentId AgentId, ModelProviderId? Host, ModelId Model,
    int ExcludedRecords, bool HasGaps);
internal sealed record UsageExactElapsed(int Index, DailyUsageRollup Usage);
internal sealed record UsageConfigurationRollup(ModelId? ObservedModel, string? Effort, string? Tier,
    DailyUsageRollup Usage, int? Hour, int? ElapsedIndex, UsageTimePrecision Precision, UsageRecordKind Kind,
    UsageComponentAvailability InputSplit, UsageDetailMetadata DetailMetadata);

public sealed record UsageObservedConfiguration(AgentId AgentId, ModelProviderId? Host, ModelId Model,
    ModelId? ObservedModel, string? Effort, string? Tier, UsageReportMetrics Metrics);
public sealed record UsageConfigurationCoverage(int RetainedRecords, int AvailableRecords,
    int MissingRecords, DateOnly? FirstDate, DateOnly? LastDate);

public sealed record UsageActivityCoverage(int TimestampedRecords, long TimestampedTokens,
    int IntervalRecords, long IntervalTokens, int UnplaceableRecords, long UnplaceableTokens,
    int MissingDetailRecords, long MissingDetailTokens);
public sealed record UsageActivityBucket(DateOnly Date, string TimeZoneId, AgentId AgentId,
    ModelProviderId? Host, ModelId ModelId, int Hour, int Records, long Tokens);

public sealed record UsageCacheComposition(long CacheReadTokens, long EligibleInputTokens,
    int EligibleRecords, int UnknownComponentRecords, int UnavailableComponentRecords, int MissingDetailRecords)
{
    public string MethodId { get; init; } = "measured-input-cache-share/v1";
    public decimal? CacheReadPercent => EligibleInputTokens > 0
        ? (decimal)CacheReadTokens / EligibleInputTokens * 100m : null;
}

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

public sealed record UsageModelChartGroup(UsageModelReport? Model, UsageReport Report, int ModelCount)
{
    public bool IsOther => Model is null;
}

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
    string? Effort, string? Tier, DateTimeOffset FirstObservedAtUtc, DateTimeOffset LastObservedAtUtc, long Tokens)
{
    public ModelProviderId? ModelProviderId { get; init; }
}

/// <summary>Empty dimensions mean all. A null host selects unknown hosts only.
/// Search matches retained tool, host, and canonical model IDs; it changes the analytical population.</summary>
public sealed record UsageReportSelection
{
    public IReadOnlyList<AgentId> Agents { get; init; } = [];
    public IReadOnlyList<ModelProviderId?> ModelProviders { get; init; } = [];
    public IReadOnlyList<ModelId> Models { get; init; } = [];
    public IReadOnlyList<ModelId?> ObservedModels { get; init; } = [];
    public IReadOnlyList<string?> ReasoningEfforts { get; init; } = [];
    public IReadOnlyList<string?> ServiceTiers { get; init; } = [];
    public bool HasConfigurationFilters => ObservedModels.Count > 0 || ReasoningEfforts.Count > 0 || ServiceTiers.Count > 0;
    public string Search { get; init; } = string.Empty;

    internal bool IncludesConfiguration(ModelId? observed, string? effort, string? tier) =>
        (ObservedModels.Count == 0 || ObservedModels.Contains(observed))
        && (ReasoningEfforts.Count == 0 || ReasoningEfforts.Contains(effort, StringComparer.Ordinal))
        && (ServiceTiers.Count == 0 || ServiceTiers.Contains(tier, StringComparer.Ordinal));

    internal bool Includes(AgentId agent, ModelProviderId? host, ModelId model)
    {
        string search = Search.Trim();
        return (Agents.Count == 0 || Agents.Contains(agent))
            && (ModelProviders.Count == 0 || ModelProviders.Contains(host))
            && (Models.Count == 0 || Models.Contains(model))
            && (search.Length == 0 || agent.Value.Contains(search, StringComparison.OrdinalIgnoreCase)
                || host?.Value.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                || model.Value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record UsageElapsedBucket(int Index, AgentId AgentId, UsageReportMetrics Metrics);

public sealed record UsageReport(
    UsageReportMetrics Totals,
    IReadOnlyList<UsageAgentReport> Agents,
    IReadOnlyList<UsageModelReport> Models,
    IReadOnlyList<UsageDayReport> Days,
    IReadOnlyList<UsageAgentDayReport> AgentDays,
    IReadOnlyList<UsageModelDayReport> ModelDays)
{
    internal IReadOnlyList<DailyUsageRollup> Rollups { get; init; } = [];
    internal IReadOnlyList<UsageConfigurationRollup> ConfigurationRollups { get; init; } = [];
    internal IReadOnlyList<UsageConfigurationRollup> ExcludedConfigurations { get; init; } = [];
    internal IReadOnlyList<UsageExactTiming> ConfigurationHistoryGaps { get; init; } = [];
    public bool HasConfigurationDetails { get; init; }
    public IReadOnlyList<UsageObservedConfiguration> Configurations { get; init; } = [];
    public UsageConfigurationCoverage? ConfigurationCoverage { get; init; }
    public UsageActivityCoverage? ActivityCoverage { get; init; }
    public UsageCacheComposition? CacheComposition { get; init; }
    public IReadOnlyList<UsageActivityBucket> ActivityTimeBuckets { get; init; } = [];
    public UsageReportSelection? ConfigurationSelection { get; init; }
    public UsageDataRevision? DataRevision { get; init; }
    public IReadOnlyList<UsageTimeRollup> TimeBuckets { get; init; } = [];
    public bool HasTimeBucketDetails { get; init; }

    public IReadOnlyList<UsageElapsedBucket> ElapsedTwoHourBuckets { get; init; } = [];
    internal IReadOnlyList<UsageExactTiming> ExactTiming { get; init; } = [];
    internal IReadOnlyList<UsageExactElapsed> ExactElapsed { get; init; } = [];

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

public sealed partial class UsageReportQuery
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
        bool includeModelProfiles = false,
        bool includeConfigurations = false,
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        UsageReportReadSnapshot snapshot = includeModelProfiles || includeConfigurations
            ? await repository.ReadReportSnapshotAsync(fromInclusive, toInclusive,
                new DateTimeOffset(fromInclusive.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                new DateTimeOffset(toInclusive.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                agentId, includeTimeBuckets: includeTimeBuckets, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await repository.ReadDailyReportSnapshotAsync(
                fromInclusive, toInclusive, agentId, includeTimeBuckets, cancellationToken).ConfigureAwait(false);
        UsageReport report = Build(snapshot.Rollups) with
        {
            PricingVersions = snapshot.PricingVersions,
            DataRevision = snapshot.DataRevision,
            ParserVersions = snapshot.ParserVersions,
            AccountUsage = snapshot.AccountUsage,
            CollectionState = snapshot.CollectionState.Where(row => agentId is null || row.AgentId == agentId.Value).ToArray(),
            ModelProfiles = includeModelProfiles ? BuildModelProfiles(snapshot.Events, fromInclusive, toInclusive) : [],
        };
        if (includeConfigurations)
        {
            UsageConfigurationRollup[] configurations = BuildConfigurationRollups(snapshot.Events, fromInclusive, toInclusive);
            report = report with
            {
                HasConfigurationDetails = true,
                ConfigurationRollups = configurations,
                Configurations = BuildConfigurations(configurations),
                ConfigurationCoverage = ConfigurationCoverage(report.Totals.EventCount, configurations),
                ActivityCoverage = BuildActivityCoverage(report.Totals.EventCount, report.Totals.Tokens.Total, configurations),
                CacheComposition = BuildCacheComposition(report.Totals.EventCount, configurations),
                ActivityTimeBuckets = BuildActivityBuckets(configurations),
            };
        }
        if (!includeTimeBuckets) return report;
        IReadOnlyList<UsageTimeRollup> buckets = snapshot.TimeBuckets;
        int excluded = Math.Max(0, report.Totals.EventCount - buckets.Sum(item => item.Usage.EventCount));
        return report with { TimeBuckets = buckets, HasTimeBucketDetails = true, HasTimingGaps = excluded > 0, ExcludedTimingRecords = excluded };
    }

    /// <summary>
    /// Reads events in a UTC half-open range. Reset-cycle reports use this path so activity on
    /// a reset date stays in the cycle that contains its event timestamp.
    /// </summary>
    public async Task<UsageReport> ReadExactAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId? agentId = null,
        bool includeConfigurations = false,
        CancellationToken cancellationToken = default)
    {
        if (toExclusiveUtc <= fromInclusiveUtc) throw new ArgumentException("The exact interval must have positive duration.", nameof(toExclusiveUtc));
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        DateOnly firstDate = DateOnly.FromDateTime(fromInclusiveUtc.UtcDateTime.AddDays(-1));
        DateOnly lastDate = DateOnly.FromDateTime(toExclusiveUtc.UtcDateTime.AddDays(1));
        UsageReportReadSnapshot snapshot = await repository.ReadReportSnapshotAsync(
            firstDate, lastDate, fromInclusiveUtc.AddDays(-1), toExclusiveUtc.AddDays(1),
            agentId, includeOverlappingIntervals: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UsageEvent> candidates = snapshot.Events;
        UsageEvent[] events = candidates.Where(item =>
            item.OccurredAtUtc >= fromInclusiveUtc && item.OccurredAtUtc < toExclusiveUtc
            && item.DetailMetadata.RecordKind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate)
            && (item.TimePrecision == UsageTimePrecision.Timestamp
                || item.TimePrecision == UsageTimePrecision.Interval && item.IntervalStartedAtUtc >= fromInclusiveUtc)).ToArray();
        var acceptedKeys = events.Select(item => item.EventKey).ToHashSet();
        UsageEvent[] excludedEvents = candidates.Where(item =>
        {
            if (acceptedKeys.Contains(item.EventKey) || item.TimePrecision == UsageTimePrecision.Timestamp
                && item.DetailMetadata.RecordKind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate)) return false;
            if (item.TimePrecision == UsageTimePrecision.Interval)
                return item.IntervalStartedAtUtc < toExclusiveUtc && item.OccurredAtUtc >= fromInclusiveUtc;
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId);
            DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAtUtc, zone).DateTime);
            return date >= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusiveUtc, zone).DateTime)
                && date <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toExclusiveUtc.AddTicks(-1), zone).DateTime);
        }).ToArray();
        IReadOnlyList<DailyUsageRollup> retained = snapshot.Rollups;
        // Daily-only retained history cannot establish an exact interval, even when raw events are gone.
        var supportedCounts = candidates.Where(item => item.TimePrecision is UsageTimePrecision.Timestamp or UsageTimePrecision.Interval
                && item.DetailMetadata.RecordKind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate))
            .GroupBy(item => (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId)).DateTime),
                item.GroupingTimeZoneId, item.AgentId, item.ModelProviderId, item.ModelId))
            .ToDictionary(group => group.Key, group => group.Count());
        DailyUsageRollup[] gapRows = retained.Where(row =>
        {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(row.GroupingTimeZoneId);
            DateOnly from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusiveUtc, zone).DateTime);
            DateOnly to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toExclusiveUtc.AddTicks(-1), zone).DateTime);
            return row.Date >= from && row.Date <= to && row.EventCount > supportedCounts.GetValueOrDefault(
                (row.Date, row.GroupingTimeZoneId, row.AgentId, row.ModelProviderId, row.ModelId));
        }).ToArray();
        var excludedByModel = excludedEvents.GroupBy(row => (row.AgentId, row.ModelProviderId, row.ModelId))
            .ToDictionary(group => group.Key, group => group.Count());
        var gapModels = gapRows.Select(row => (row.AgentId, row.ModelProviderId, row.ModelId)).ToHashSet();
        UsageExactTiming[] timing = excludedByModel.Keys.Union(gapModels).Select(key => new UsageExactTiming(
            key.AgentId, key.ModelProviderId, key.ModelId, excludedByModel.GetValueOrDefault(key),
            gapModels.Contains(key))).ToArray();
        UsageExactElapsed[] elapsed = events.Where(item => item.TimePrecision == UsageTimePrecision.Timestamp)
            .GroupBy(item => (int)((item.OccurredAtUtc - fromInclusiveUtc).Ticks / TimeSpan.FromHours(2).Ticks))
            .SelectMany(group => UsageRollupAggregator.Aggregate(group)
                .Select(row => new UsageExactElapsed(group.Key, row))).ToArray();
        UsageConfigurationRollup[] configurations = includeConfigurations
            ? BuildConfigurationRollups(events, firstDate, lastDate, fromInclusiveUtc) : [];
        var availableCounts = includeConfigurations ? candidates.GroupBy(item =>
                (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.OccurredAtUtc,
                    TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId)).DateTime),
                    item.GroupingTimeZoneId, item.AgentId, item.ModelProviderId, item.ModelId))
            .ToDictionary(group => group.Key, group => group.Count()) : null;
        return Build(UsageRollupAggregator.Aggregate(events)) with
        {
            HasConfigurationDetails = includeConfigurations,
            ConfigurationRollups = configurations,
            Configurations = BuildConfigurations(configurations),
            ConfigurationCoverage = includeConfigurations ? ConfigurationCoverage(events.Length, configurations) : null,
            ActivityCoverage = includeConfigurations ? BuildActivityCoverage(events.Length, events.Sum(row => row.Tokens.Total), configurations) : null,
            CacheComposition = includeConfigurations ? BuildCacheComposition(events.Length, configurations) : null,
            ActivityTimeBuckets = includeConfigurations ? BuildActivityBuckets(configurations) : [],
            ExcludedConfigurations = includeConfigurations
                ? BuildConfigurationRollups(excludedEvents, DateOnly.MinValue, DateOnly.MaxValue) : [],
            ConfigurationHistoryGaps = includeConfigurations ? gapRows.Where(row => row.EventCount > availableCounts!.GetValueOrDefault(
                    (row.Date, row.GroupingTimeZoneId, row.AgentId, row.ModelProviderId, row.ModelId)))
                .Select(row => new UsageExactTiming(row.AgentId, row.ModelProviderId, row.ModelId, 0, true)).Distinct().ToArray() : [],
            ExcludedTimingRecords = excludedEvents.Length,
            DataRevision = snapshot.DataRevision,
            ExactTiming = timing,
            ExactElapsed = elapsed,
            PricingVersions = events.Select(item => item.Cost.CatalogVersion).OfType<string>().Distinct().Order().ToArray(),
            ParserVersions = events.Select(item => item.ParserVersion).Distinct().Order().ToArray(),
            HasTimingGaps = gapRows.Length > 0 || excludedEvents.Length > 0,
            CollectionState = snapshot.CollectionState.Where(row => agentId is null || row.AgentId == agentId.Value).ToArray(),
            IsExactInterval = true,
            HasTimeBucketDetails = true,
            ElapsedTwoHourBuckets = BuildElapsedBuckets(elapsed),
            TimeBuckets = events.Where(item => item.TimePrecision == UsageTimePrecision.Timestamp).GroupBy(item =>
            {
                DateTime local = TimeZoneInfo.ConvertTime(item.OccurredAtUtc,
                    TimeZoneInfo.FindSystemTimeZoneById(item.GroupingTimeZoneId)).DateTime;
                return (DateOnly.FromDateTime(local), Hour: local.Hour / 2 * 2);
            }).SelectMany(group => UsageRollupAggregator.Aggregate(group)
                .Select(rollup => new UsageTimeRollup(rollup, group.Key.Hour))).ToArray(),
        };
    }

    private static UsageElapsedBucket[] BuildElapsedBuckets(IEnumerable<UsageExactElapsed> rows) =>
        rows.GroupBy(row => (row.Index, row.Usage.AgentId))
            .Select(group => new UsageElapsedBucket(group.Key.Index, group.Key.AgentId,
                Aggregate(group.Select(row => row.Usage)))).ToArray();

    private static UsageConfigurationCoverage ConfigurationCoverage(int retained, UsageConfigurationRollup[] rows)
    {
        int available = rows.Sum(row => row.Usage.EventCount);
        return new(retained, available, Math.Max(0, retained - available),
            rows.Length == 0 ? null : rows.Min(row => row.Usage.Date),
            rows.Length == 0 ? null : rows.Max(row => row.Usage.Date));
    }

    private static UsageObservedConfiguration[] BuildConfigurations(IEnumerable<UsageConfigurationRollup> rows) =>
        rows.GroupBy(row => (row.Usage.AgentId, row.Usage.ModelProviderId, row.Usage.ModelId,
                row.ObservedModel, row.Effort, row.Tier))
            .Select(group => new UsageObservedConfiguration(group.Key.AgentId, group.Key.ModelProviderId,
                group.Key.ModelId, group.Key.ObservedModel, group.Key.Effort, group.Key.Tier,
                Aggregate(group.Select(row => row.Usage))))
            .OrderBy(row => row.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(row => row.Host?.Value, StringComparer.Ordinal)
            .ThenBy(row => row.Model.Value, StringComparer.Ordinal)
            .ThenBy(row => row.ObservedModel?.Value, StringComparer.Ordinal)
            .ThenBy(row => row.Effort, StringComparer.Ordinal)
            .ThenBy(row => row.Tier, StringComparer.Ordinal).ToArray();

    internal static UsageConfigurationRollup[] BuildConfigurationRollups(
        IReadOnlyList<UsageEvent> events, DateOnly from, DateOnly to, DateTimeOffset? exactStart = null)
    {
        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        return events.Select(row =>
            {
                if (!zones.TryGetValue(row.GroupingTimeZoneId, out TimeZoneInfo? zone))
                    zones[row.GroupingTimeZoneId] = zone = TimeZoneInfo.FindSystemTimeZoneById(row.GroupingTimeZoneId);
                return (Event: row, Local: TimeZoneInfo.ConvertTime(row.OccurredAtUtc, zone));
            }).Where(row => DateOnly.FromDateTime(row.Local.DateTime) >= from
                && DateOnly.FromDateTime(row.Local.DateTime) <= to)
            .GroupBy(row => (row.Event.ObservedModelId, row.Event.ReasoningEffort, row.Event.ServiceTier,
                row.Event.TimePrecision, row.Event.DetailMetadata.RecordKind,
                Input: row.Event.DetailMetadata.Input, Output: row.Event.DetailMetadata.Output,
                Reasoning: row.Event.DetailMetadata.Reasoning, CacheRead: row.Event.DetailMetadata.CacheRead,
                CacheWrite: row.Event.DetailMetadata.CacheWrite,
                InputSplit: InputSplitAvailability(row.Event.DetailMetadata),
                Hour: row.Event.TimePrecision == UsageTimePrecision.Timestamp
                    && row.Event.DetailMetadata.RecordKind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate)
                    ? (int?)(row.Local.Hour / 2 * 2) : null,
                Elapsed: row.Event.TimePrecision == UsageTimePrecision.Timestamp
                    && row.Event.DetailMetadata.RecordKind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate) && exactStart.HasValue
                    ? (int?)((row.Event.OccurredAtUtc - exactStart.Value).Ticks / TimeSpan.FromHours(2).Ticks) : null))
            .SelectMany(group => UsageRollupAggregator.Aggregate(group.Select(row => row.Event))
                .Select(row => new UsageConfigurationRollup(group.Key.ObservedModelId,
                    group.Key.ReasoningEffort, group.Key.ServiceTier, row, group.Key.Hour, group.Key.Elapsed,
                    group.Key.TimePrecision, group.Key.RecordKind, group.Key.InputSplit,
                    new UsageDetailMetadata(input: group.Key.Input, output: group.Key.Output,
                        reasoning: group.Key.Reasoning, cacheRead: group.Key.CacheRead,
                        cacheWrite: group.Key.CacheWrite)))).ToArray();
    }

    private static UsageComponentAvailability InputSplitAvailability(UsageDetailMetadata detail) =>
        detail.Input == UsageComponentAvailability.Measured && detail.CacheRead == UsageComponentAvailability.Measured
            && detail.CacheWrite == UsageComponentAvailability.Measured ? UsageComponentAvailability.Measured
        : detail.Input == UsageComponentAvailability.Unavailable || detail.CacheRead == UsageComponentAvailability.Unavailable
            || detail.CacheWrite == UsageComponentAvailability.Unavailable ? UsageComponentAvailability.Unavailable
        : UsageComponentAvailability.Unknown;

    private static UsageCacheComposition BuildCacheComposition(int totalRecords, IReadOnlyList<UsageConfigurationRollup> rows)
    {
        long cacheRead = 0, input = 0;
        int eligible = 0, unknown = 0, unavailable = 0;
        foreach (UsageConfigurationRollup row in rows)
        {
            if (row.InputSplit == UsageComponentAvailability.Measured)
            {
                cacheRead = checked(cacheRead + row.Usage.Tokens.CacheRead);
                input = checked(input + row.Usage.Tokens.Input + row.Usage.Tokens.CacheRead + row.Usage.Tokens.CacheWrite);
                eligible = checked(eligible + row.Usage.EventCount);
            }
            else if (row.InputSplit == UsageComponentAvailability.Unavailable)
                unavailable = checked(unavailable + row.Usage.EventCount);
            else unknown = checked(unknown + row.Usage.EventCount);
        }
        return new(cacheRead, input, eligible, unknown, unavailable, Math.Max(0, totalRecords - eligible - unknown - unavailable));
    }

    private static bool IsActivityTimestamp(UsageConfigurationRollup row) =>
        row.Precision == UsageTimePrecision.Timestamp && row.Kind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate);

    private static UsageActivityBucket[] BuildActivityBuckets(IReadOnlyList<UsageConfigurationRollup> rows) =>
        rows.Where(row => IsActivityTimestamp(row) && row.Hour.HasValue)
            .Select(row => new UsageActivityBucket(row.Usage.Date, row.Usage.GroupingTimeZoneId, row.Usage.AgentId,
                row.Usage.ModelProviderId, row.Usage.ModelId, row.Hour!.Value, row.Usage.EventCount, row.Usage.Tokens.Total)).ToArray();

    private static UsageActivityCoverage BuildActivityCoverage(int totalRecords, long totalTokens,
        IReadOnlyList<UsageConfigurationRollup> rows)
    {
        int timestamped = 0, intervals = 0, unplaceable = 0;
        long timestampedTokens = 0, intervalTokens = 0, unplaceableTokens = 0;
        foreach (UsageConfigurationRollup row in rows)
        {
            if (IsActivityTimestamp(row))
            {
                timestamped = checked(timestamped + row.Usage.EventCount);
                timestampedTokens = checked(timestampedTokens + row.Usage.Tokens.Total);
            }
            else if (row.Precision == UsageTimePrecision.Interval && row.Kind is not (UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate))
            {
                intervals = checked(intervals + row.Usage.EventCount);
                intervalTokens = checked(intervalTokens + row.Usage.Tokens.Total);
            }
            else
            {
                unplaceable = checked(unplaceable + row.Usage.EventCount);
                unplaceableTokens = checked(unplaceableTokens + row.Usage.Tokens.Total);
            }
        }
        return new(timestamped, timestampedTokens, intervals, intervalTokens, unplaceable, unplaceableTokens,
            Math.Max(0, totalRecords - timestamped - intervals - unplaceable),
            Math.Max(0, totalTokens - timestampedTokens - intervalTokens - unplaceableTokens));
    }

    private static UsageModelProfile[] BuildModelProfiles(
        IReadOnlyList<UsageEvent> events, DateOnly from, DateOnly to) =>
        events.Where(row =>
        {
            if (row.TimePrecision != UsageTimePrecision.Timestamp
                || row.DetailMetadata.RecordKind is UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate) return false;
            DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.OccurredAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById(row.GroupingTimeZoneId)).DateTime);
            return date >= from && date <= to;
        }).GroupBy(row => (row.AgentId.Value, row.ModelProviderId, Model: row.ModelId.Value,
            Raw: row.ObservedModelId?.Value, row.ReasoningEffort, row.ServiceTier))
        .Select(group => new UsageModelProfile(group.Key.Value, group.Key.Model, group.Key.Raw,
            group.Key.ReasoningEffort, group.Key.ServiceTier, group.Min(row => row.OccurredAtUtc),
            group.Max(row => row.OccurredAtUtc), group.Sum(row => row.Tokens.Total))
            { ModelProviderId = group.Key.ModelProviderId }).ToArray();

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
            .GroupBy(rollup => (rollup.AgentId, rollup.ModelProviderId, rollup.ModelId))
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
            .GroupBy(rollup => (rollup.Date, rollup.AgentId))
            .Select(group => new UsageAgentDayReport(
                group.Key.Date,
                group.Key.AgentId,
                Aggregate(group)))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ToArray();

        UsageModelDayReport[] modelDays = snapshot
            .GroupBy(rollup => (rollup.Date, rollup.AgentId, rollup.ModelProviderId, rollup.ModelId))
            .Select(group => new UsageModelDayReport(
                group.Key.Date, group.Key.AgentId, group.Key.ModelProviderId,
                group.Key.ModelId, Aggregate(group)))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ModelId.Value, StringComparer.Ordinal)
            .ToArray();
        return new UsageReport(Aggregate(snapshot), agents, models, days, agentDays, modelDays)
        {
            Rollups = snapshot,
            GroupingTimeZoneIds = snapshot.Select(row => row.GroupingTimeZoneId).Distinct().Order().ToArray(),
        };
    }

    public static UsageReportMetrics Aggregate(IEnumerable<DailyUsageRollup> rollups)
    {
        if (rollups is IList<DailyUsageRollup> { Count: 1 } single)
        {
            DailyUsageRollup row = single[0];
            return new(row.EventCount, row.Tokens, row.ReportedCostUsd, row.EstimatedCostUsd,
                row.UnpricedTokens, row.UnavailableCostEventCount, row.Coverage);
        }
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

                coverage = CoverageAggregation.Worst(coverage, rollup.Coverage);
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

}
