using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.Core.Tests.Usage;

public sealed class UsageRollupAggregatorTests
{
    [Fact]
    public void AggregatesCivilDayAndKeepsCostBucketsSeparate()
    {
        UsageEvent[] events =
        [
            CreateEvent('a', new DateTimeOffset(2026, 7, 22, 2, 30, 0, TimeSpan.Zero),
                CostObservation.ProviderReported(1.25m), CoverageKind.Complete),
            CreateEvent('b', new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero),
                CostObservation.CatalogEstimated(0.75m, "catalog-1", "grok-4.5"), CoverageKind.Partial),
            CreateEvent('c', new DateTimeOffset(2026, 7, 22, 4, 0, 0, TimeSpan.Zero),
                CostObservation.Unavailable(), CoverageKind.Unpriced),
        ];

        IReadOnlyList<DailyUsageRollup> rollups = UsageRollupAggregator.Aggregate(events);

        Assert.Equal(2, rollups.Count);
        DailyUsageRollup july21 = Assert.Single(rollups, rollup =>
            rollup.Date == new DateOnly(2026, 7, 21));
        Assert.Equal(1.25m, july21.ReportedCostUsd);
        Assert.Null(july21.EstimatedCostUsd);
        Assert.Equal(0, july21.UnavailableCostEventCount);

        DailyUsageRollup july22 = Assert.Single(rollups, rollup =>
            rollup.Date == new DateOnly(2026, 7, 22));
        Assert.Null(july22.ReportedCostUsd);
        Assert.Equal(0.75m, july22.EstimatedCostUsd);
        Assert.Equal(150, july22.UnpricedTokens);
        Assert.Equal(1, july22.UnavailableCostEventCount);
        Assert.Equal(2, july22.EventCount);
        Assert.Equal(300, july22.Tokens.Total);
        Assert.Equal(CoverageKind.Unpriced, july22.Coverage);
    }

    private static UsageEvent CreateEvent(
        char keyCharacter,
        DateTimeOffset occurredAtUtc,
        CostObservation cost,
        CoverageKind coverage) =>
        new(
            new UsageEventKey(new string(keyCharacter, 64)),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5"),
            occurredAtUtc,
            "Argentina Standard Time",
            new TokenBreakdown(100, 25, 5, 20, 0),
            cost,
            "fixture/1",
            coverage);
}
