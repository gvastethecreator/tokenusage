using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageAttributionExportTests
{
    [Fact]
    public async Task RecheckConsentStripsAttributionWhenDisabledWithoutChangingTotals()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report) with
        {
            Sessions = [new("s1", "attributed", "100", "100", "1", "1")],
            Projects = [new("p1", "attributed", "100", "100", "1", "1")],
        };

        UsageReportSnapshotV2.Document kept = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexSession),
                Disabled(AttributionCapability.CursorSession),
                Enabled(AttributionCapability.CodexProject)));
        Assert.NotNull(kept.Sessions);
        Assert.NotNull(kept.Projects);
        Assert.Equal(frozen.Totals, kept.Totals);

        UsageReportSnapshotV2.Document stripped = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexSession),
                Disabled(AttributionCapability.CursorSession),
                Disabled(AttributionCapability.CodexProject)));
        Assert.Null(stripped.Sessions);
        Assert.Null(stripped.Projects);
        Assert.Equal(frozen.Totals, stripped.Totals);
        Assert.Equal("100", stripped.Totals.Tokens.Input);
    }

    private static AttributionConsent Enabled(AttributionCapability capability) =>
        new(capability, AttributionConsentState.Enabled, 1, new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private static AttributionConsent Disabled(AttributionCapability capability) =>
        new(capability, AttributionConsentState.Disabled, 0, new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private sealed class MapConsent(params AttributionConsent[] consents) : IAttributionConsentSource
    {
        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                consents.FirstOrDefault(item => item.Capability.Value == capability.Value)
                ?? new AttributionConsent(
                    capability,
                    AttributionConsentState.Disabled,
                    0,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)));
    }
}
