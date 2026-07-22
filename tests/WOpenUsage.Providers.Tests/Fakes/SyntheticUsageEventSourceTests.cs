using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.Providers.Tests.Fakes;

public sealed class SyntheticUsageEventSourceTests
{
    [Fact]
    public async Task FixtureCoversEveryCostKindAndAStableDuplicate()
    {
        var source = new SyntheticUsageEventSource(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero)),
            "Argentina Standard Time");

        UsageSourceReadResult result = await source.ReadAsync();
        IReadOnlyList<UsageEvent> events = result.Events;

        Assert.Equal(SourceKind.Synthetic, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(4, events.Count);
        Assert.Contains(events, usageEvent => usageEvent.Cost.Kind == CostKind.ProviderReported);
        Assert.Contains(events, usageEvent => usageEvent.Cost.Kind == CostKind.CatalogEstimated);
        Assert.Contains(events, usageEvent => usageEvent.Cost.Kind == CostKind.Unavailable);
        Assert.Equal(events[0].EventKey, events[1].EventKey);
        Assert.All(events, usageEvent => Assert.Equal("fixture/1", usageEvent.ParserVersion));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
