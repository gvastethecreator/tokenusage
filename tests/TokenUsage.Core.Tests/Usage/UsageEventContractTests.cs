using System.Reflection;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageEventContractTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CostKindsKeepReportedEstimatedAndUnavailableSeparate()
    {
        CostObservation reported = CostObservation.ProviderReported(1.25m);
        CostObservation estimated = CostObservation.CatalogEstimated(
            0.75m,
            "catalog-2026-07",
            "grok-4.5");
        CostObservation unavailable = CostObservation.Unavailable();

        Assert.Equal(CostKind.ProviderReported, reported.Kind);
        Assert.Equal(1.25m, reported.ReportedCostUsd);
        Assert.Null(reported.EstimatedCostUsd);
        Assert.Equal(CostKind.CatalogEstimated, estimated.Kind);
        Assert.Null(estimated.ReportedCostUsd);
        Assert.Equal(0.75m, estimated.EstimatedCostUsd);
        Assert.Equal(CostKind.Unavailable, unavailable.Kind);
        Assert.Null(unavailable.ReportedCostUsd);
        Assert.Null(unavailable.EstimatedCostUsd);
    }

    [Fact]
    public void EventRejectsNonUtcTimestampsAndNegativeTokens()
    {
        Assert.Throws<ArgumentException>(() => CreateEvent(
            occurredAtUtc: OccurredAt.ToOffset(TimeSpan.FromHours(-3))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBreakdown(
            input: -1,
            output: 0,
            reasoning: 0,
            cacheRead: 0,
            cacheWrite: 0));
    }

    [Fact]
    public void TokenTotalFailsClosedOnOverflow()
    {
        var tokens = new TokenBreakdown(
            long.MaxValue,
            output: 1,
            reasoning: 0,
            cacheRead: 0,
            cacheWrite: 0);

        Assert.Throws<OverflowException>(() => _ = tokens.Total);
    }

    [Fact]
    public void EventPublicSurfaceCannotStoreCustomerContentOrLocalIdentity()
    {
        string[] expectedProperties =
        [
            "EventKey",
            "AgentId",
            "ModelProviderId",
            "ModelId",
            "OccurredAtUtc",
            "GroupingTimeZoneId",
            "Tokens",
            "Cost",
            "ParserVersion",
            "Coverage",
        ];
        string[] forbiddenTerms =
        [
            "Prompt", "Response", "Project", "Task", "Tool", "Command",
            "Session", "Path", "Account", "Transcript", "Content", "Text",
        ];

        string[] actualProperties = typeof(UsageEvent)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties.Order(StringComparer.Ordinal), actualProperties);
        Assert.DoesNotContain(
            actualProperties,
            property => forbiddenTerms.Any(term =>
                property.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static UsageEvent CreateEvent(DateTimeOffset? occurredAtUtc = null) =>
        new(
            new UsageEventKey(new string('a', 64)),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5"),
            occurredAtUtc ?? OccurredAt,
            "Argentina Standard Time",
            new TokenBreakdown(100, 25, 5, 10, 0),
            CostObservation.ProviderReported(0.25m),
            "fixture/1",
            CoverageKind.Complete);
}
