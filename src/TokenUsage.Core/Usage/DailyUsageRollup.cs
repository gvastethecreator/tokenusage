using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed record DailyUsageRollup
{
    public DailyUsageRollup(
        DateOnly date,
        string groupingTimeZoneId,
        AgentId agentId,
        ModelProviderId? modelProviderId,
        ModelId modelId,
        TokenBreakdown tokens,
        decimal? reportedCostUsd,
        decimal? estimatedCostUsd,
        long unpricedTokens,
        int unavailableCostEventCount,
        int eventCount,
        CoverageKind coverage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(reportedCostUsd ?? 0m);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedCostUsd ?? 0m);
        ArgumentOutOfRangeException.ThrowIfNegative(unpricedTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(unavailableCostEventCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(eventCount, 0);
        if (unpricedTokens > tokens.Total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unpricedTokens),
                "Unpriced tokens cannot exceed total tokens.");
        }

        if (unavailableCostEventCount > eventCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unavailableCostEventCount),
                "Unavailable cost events cannot exceed total events.");
        }

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(coverage));
        }

        Date = date;
        GroupingTimeZoneId = groupingTimeZoneId;
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        ModelProviderId = modelProviderId;
        ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        ReportedCostUsd = reportedCostUsd;
        EstimatedCostUsd = estimatedCostUsd;
        UnpricedTokens = unpricedTokens;
        UnavailableCostEventCount = unavailableCostEventCount;
        EventCount = eventCount;
        Coverage = coverage;
    }

    public DateOnly Date { get; }

    public string GroupingTimeZoneId { get; }

    public AgentId AgentId { get; }

    public ModelProviderId? ModelProviderId { get; }

    public ModelId ModelId { get; }

    public TokenBreakdown Tokens { get; }

    public decimal? ReportedCostUsd { get; }

    public decimal? EstimatedCostUsd { get; }

    public long UnpricedTokens { get; }

    public int UnavailableCostEventCount { get; }

    public int EventCount { get; }

    public CoverageKind Coverage { get; }
}

public static class UsageRollupAggregator
{
    public static IReadOnlyList<DailyUsageRollup> Aggregate(IEnumerable<UsageEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var accumulators = new Dictionary<RollupKey, Accumulator>();

        foreach (UsageEvent usageEvent in events)
        {
            ArgumentNullException.ThrowIfNull(usageEvent);
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(
                usageEvent.GroupingTimeZoneId);
            DateOnly date = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(usageEvent.OccurredAtUtc, zone).DateTime);
            var key = new RollupKey(
                date,
                usageEvent.GroupingTimeZoneId,
                usageEvent.AgentId,
                usageEvent.ModelProviderId,
                usageEvent.ModelId);

            if (!accumulators.TryGetValue(key, out Accumulator? accumulator))
            {
                accumulator = new Accumulator();
                accumulators.Add(key, accumulator);
            }

            accumulator.Add(usageEvent);
        }

        return accumulators
            .OrderBy(pair => pair.Key.Date)
            .ThenBy(pair => pair.Key.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.ModelId.Value, StringComparer.Ordinal)
            .Select(pair => pair.Value.ToRollup(pair.Key))
            .ToArray();
    }

    private sealed record RollupKey(
        DateOnly Date,
        string GroupingTimeZoneId,
        AgentId AgentId,
        ModelProviderId? ModelProviderId,
        ModelId ModelId);

    private sealed class Accumulator
    {
        private long _input;
        private long _output;
        private long _reasoning;
        private long _cacheRead;
        private long _cacheWrite;
        private decimal _reported;
        private decimal _estimated;
        private int _reportedCount;
        private int _estimatedCount;
        private int _unavailableCount;
        private long _unpricedTokens;
        private int _eventCount;
        private CoverageKind _coverage = CoverageKind.Complete;

        public void Add(UsageEvent usageEvent)
        {
            checked
            {
                _input += usageEvent.Tokens.Input;
                _output += usageEvent.Tokens.Output;
                _reasoning += usageEvent.Tokens.Reasoning;
                _cacheRead += usageEvent.Tokens.CacheRead;
                _cacheWrite += usageEvent.Tokens.CacheWrite;
                _eventCount++;

                switch (usageEvent.Cost.Kind)
                {
                    case CostKind.ProviderReported:
                        _reported += usageEvent.Cost.ReportedCostUsd!.Value;
                        _reportedCount++;
                        break;
                    case CostKind.CatalogEstimated:
                        _estimated += usageEvent.Cost.EstimatedCostUsd!.Value;
                        _estimatedCount++;
                        break;
                    case CostKind.Unavailable:
                        _unavailableCount++;
                        _unpricedTokens += usageEvent.Tokens.Total;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(usageEvent),
                            "Unknown cost kind.");
                }
            }

            if (CoverageRank(usageEvent.Coverage) > CoverageRank(_coverage))
            {
                _coverage = usageEvent.Coverage;
            }
        }

        public DailyUsageRollup ToRollup(RollupKey key) =>
            new(
                key.Date,
                key.GroupingTimeZoneId,
                key.AgentId,
                key.ModelProviderId,
                key.ModelId,
                new TokenBreakdown(_input, _output, _reasoning, _cacheRead, _cacheWrite),
                _reportedCount == 0 ? null : _reported,
                _estimatedCount == 0 ? null : _estimated,
                _unpricedTokens,
                _unavailableCount,
                _eventCount,
                _coverage);

        private static int CoverageRank(CoverageKind coverage) => coverage switch
        {
            CoverageKind.Complete => 0,
            CoverageKind.Partial => 1,
            CoverageKind.SummaryOnly => 2,
            CoverageKind.Unpriced => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
        };
    }
}
