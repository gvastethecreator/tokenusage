using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

/// <summary>Reprices supported observations without dropping the unpriced historical remainder.</summary>
public static class UsageReferencePricing
{
    public static UsageReport Apply(UsageReport original, IEnumerable<UsageEvent> observations,
        Func<UsageEvent, CostObservation> resolve)
    {
        UsageEvent[] eligible = observations.Where(row => row.TimePrecision == UsageTimePrecision.Timestamp
            && row.ServiceTier is null or "standard").Select(row => new UsageEvent(row.EventKey,
                row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc, row.GroupingTimeZoneId,
                row.Tokens, resolve(row), row.ParserVersion, CoverageKind.Partial,
                row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort, row.ServiceTier)).ToArray();
        var priced = UsageRollupAggregator.Aggregate(eligible)
            .GroupBy(row => (row.Date, row.AgentId, row.ModelProviderId, row.ModelId))
            .ToDictionary(group => group.Key, group => UsageReportQuery.Aggregate(group));
        string zone = original.GroupingTimeZoneIds.Count == 1 ? original.GroupingTimeZoneIds[0] : "mixed-or-unknown";
        UsageReport result = UsageReportQuery.Build(original.ModelDays.Select(row =>
        {
            priced.TryGetValue((row.Date, row.AgentId, row.ModelProviderId, row.ModelId), out UsageReportMetrics? cohort);
            long supported = cohort is null ? 0 : cohort.Tokens.Total - cohort.UnpricedTokens;
            // A changed or incomplete source must not price more tokens than this saved projection contains.
            if (supported > row.Metrics.Tokens.Total) { cohort = null; supported = 0; }
            return new DailyUsageRollup(row.Date, zone, row.AgentId, row.ModelProviderId, row.ModelId,
                row.Metrics.Tokens, null, cohort?.EstimatedCostUsd, row.Metrics.Tokens.Total - supported,
                Math.Clamp(row.Metrics.EventCount - (cohort?.EventCount - cohort?.UnavailableCostEventCount ?? 0), 0, row.Metrics.EventCount),
                row.Metrics.EventCount, supported == 0 ? CoverageKind.Unpriced : CoverageKind.Partial);
        }));
        return result with
        {
            AccountUsage = original.AccountUsage,
            CollectionState = original.CollectionState,
            PopulationTokens = original.PopulationTokens,
            ModelProfiles = original.ModelProfiles,
            ParserVersions = original.ParserVersions,
            GroupingTimeZoneIds = original.GroupingTimeZoneIds,
            IsExactInterval = original.IsExactInterval,
            HasTimingGaps = original.HasTimingGaps,
            ExcludedTimingRecords = original.ExcludedTimingRecords,
            PriceReferenceExcludedTokens = result.Totals.UnpricedTokens,
            PricingVersions = eligible.Where(row => row.Cost.Kind == CostKind.CatalogEstimated)
                .Select(row => row.Cost.CatalogVersion!).Distinct().Order().ToArray(),
        };
    }
}
