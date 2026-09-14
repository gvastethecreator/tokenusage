using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

/// <summary>Reprices supported observations without dropping the unpriced historical remainder.</summary>
public static class UsageReferencePricing
{
    public const string FixedCohortMethodId = "fixed-cohort-repricing/v1";
    public const string FixedCohortPolicyId = "timestamp-standard-or-absent/v1";

    public static bool MatchesReferencePolicy(UsageEvent row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.TimePrecision == UsageTimePrecision.Timestamp
            && row.ServiceTier is null or "standard";
    }

    public static bool IsCatalogPriceable(CostObservation cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return cost.Kind == CostKind.CatalogEstimated && cost.EstimatedCostUsd is not null;
    }

    public static UsageRateScenarioComparison CompareCatalogDates(
        UsageReport original,
        IReadOnlyList<UsageEvent> observations,
        Func<UsageEvent, CostObservation> priceA,
        Func<UsageEvent, CostObservation> priceB)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(priceA);
        ArgumentNullException.ThrowIfNull(priceB);
        var selected = original.ModelDays
            .Select(row => (row.Date, row.AgentId, row.ModelProviderId, row.ModelId))
            .ToHashSet();
        var left = new List<UsageEvent>();
        var right = new List<UsageEvent>();
        foreach (UsageEvent row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!selected.Contains((UsageRollupAggregator.CivilDate(row), row.AgentId, row.ModelProviderId, row.ModelId)))
                continue;
            if (!MatchesConfiguration(original, row)) continue;
            if (!MatchesReferencePolicy(row)) continue;
            CostObservation pricedA = priceA(row);
            CostObservation pricedB = priceB(row);
            if (!IsCatalogPriceable(pricedA) || !IsCatalogPriceable(pricedB)) continue;
            left.Add(WithCatalogCost(row, pricedA));
            right.Add(WithCatalogCost(row, pricedB));
        }

        UsageReport baseline = Apply(original, left, row => row.Cost);
        UsageReport current = Apply(original, right, row => row.Cost);
        decimal? costA = left.Count == 0 ? null : UsageComparison.KnownCost(baseline.Totals);
        decimal? costB = left.Count == 0 ? null : UsageComparison.KnownCost(current.Totals);
        return new(FixedCohortMethodId, FixedCohortPolicyId, baseline, current, left.Count,
            left.Sum(row => row.Tokens.Total), costA, costB,
            costA is { } leftCost && costB is { } rightCost ? rightCost - leftCost : null);
    }

    public static UsageReport Apply(UsageReport original, IEnumerable<UsageEvent> observations,
        Func<UsageEvent, CostObservation> resolve)
    {
        var selected = original.ModelDays.ToDictionary(row => (row.Date, row.AgentId, row.ModelProviderId, row.ModelId), row => row.Metrics);
        UsageEvent[] eligible = observations.Where(row => selected.ContainsKey(
                (UsageRollupAggregator.CivilDate(row), row.AgentId, row.ModelProviderId, row.ModelId)))
            .Where(row => MatchesConfiguration(original, row))
            .Where(MatchesReferencePolicy).Select(row => new UsageEvent(row.EventKey,
                row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc, row.GroupingTimeZoneId,
                row.Tokens, resolve(row), row.ParserVersion, CoverageKind.Partial,
                row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort, row.ServiceTier, row.DetailMetadata)).ToArray();
        var priced = UsageRollupAggregator.Aggregate(eligible)
            .GroupBy(row => (row.Date, row.AgentId, row.ModelProviderId, row.ModelId))
            .ToDictionary(group => group.Key, group => UsageReportQuery.Aggregate(group));
        var supportedDays = priced.Where(row => row.Value.Tokens.Total - row.Value.UnpricedTokens <= selected[row.Key].Tokens.Total)
            .Select(row => row.Key).ToHashSet();
        var configurationPrices = eligible.Where(row => supportedDays.Contains(
                (UsageRollupAggregator.CivilDate(row), row.AgentId, row.ModelProviderId, row.ModelId)))
            .GroupBy(row => (row.AgentId, row.ModelProviderId, row.ModelId,
                row.ObservedModelId, row.ReasoningEffort, row.ServiceTier))
            .ToDictionary(group => group.Key, group => UsageReportQuery.Aggregate(UsageRollupAggregator.Aggregate(group)));
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
            DataRevision = original.DataRevision,
            CollectionState = original.CollectionState,
            PopulationTokens = original.PopulationTokens,
            ModelProfiles = original.ModelProfiles,
            ConfigurationSelection = original.ConfigurationSelection,
            ConfigurationCoverage = original.ConfigurationCoverage,
            ActivityCoverage = original.ActivityCoverage,
            CacheComposition = original.CacheComposition,
            ActivityTimeBuckets = original.ActivityTimeBuckets,
            Configurations = original.Configurations.Select(configuration =>
            {
                configurationPrices.TryGetValue((configuration.AgentId, configuration.Host, configuration.Model,
                    configuration.ObservedModel, configuration.Effort, configuration.Tier), out UsageReportMetrics? price);
                long supported = price is null ? 0 : price.Tokens.Total - price.UnpricedTokens;
                if (supported > configuration.Metrics.Tokens.Total) { price = null; supported = 0; }
                return configuration with { Metrics = configuration.Metrics with
                {
                    ReportedCostUsd = null, EstimatedCostUsd = price?.EstimatedCostUsd,
                    UnpricedTokens = configuration.Metrics.Tokens.Total - supported,
                    UnavailableCostEventCount = Math.Clamp(configuration.Metrics.EventCount
                        - (price?.EventCount - price?.UnavailableCostEventCount ?? 0), 0, configuration.Metrics.EventCount),
                    Coverage = supported == 0 ? CoverageKind.Unpriced : CoverageKind.Partial,
                } };
            }).ToArray(),
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

    private static bool MatchesConfiguration(UsageReport report, UsageEvent row) =>
        report.ConfigurationSelection is not { } selection
        || selection.IncludesConfiguration(row.ObservedModelId, row.ReasoningEffort, row.ServiceTier);

    private static UsageEvent WithCatalogCost(UsageEvent row, CostObservation cost) =>
        new(row.EventKey, row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc,
            row.GroupingTimeZoneId, row.Tokens, cost, row.ParserVersion, CoverageKind.Partial,
            row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort, row.ServiceTier, row.DetailMetadata);
}
