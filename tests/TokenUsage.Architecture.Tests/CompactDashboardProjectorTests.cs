using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Architecture.Tests;

public sealed class CompactDashboardProjectorTests
{
    [Fact]
    public void ReportedEstimatedUnpricedAndMissingStayDistinct()
    {
        var today = new DateOnly(2026, 8, 13);
        DailyUsageRollup[] rollups =
        [
            Rollup("codex", today, reported: 1.50m, tokens: 1_000, coverage: CoverageKind.Complete),
            Rollup("claude", today, estimated: 0.40m, tokens: 200, coverage: CoverageKind.Complete),
            Rollup(
                "grok",
                today,
                tokens: 80,
                unpriced: 80,
                coverage: CoverageKind.Unpriced),
        ];
        int providerLimitReads = 0;

        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            today,
            rollups,
            ["codex", "claude", "grok", "cursor"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key,
            getProviderLimits: id =>
            {
                providerLimitReads++;
                return id == "codex"
                    ? [new QuotaWindow("5h", 40, "40%", "4h", "Codex 5h", false)]
                    : [];
            });

        DashboardProviderSummary codex = Assert.Single(
            projection.ProviderSummaries,
            item => item.ProviderId == "codex");
        DashboardProviderSummary claude = Assert.Single(
            projection.ProviderSummaries,
            item => item.ProviderId == "claude");
        DashboardProviderSummary grok = Assert.Single(
            projection.ProviderSummaries,
            item => item.ProviderId == "grok");
        DashboardProviderSummary cursor = Assert.Single(
            projection.ProviderSummaries,
            item => item.ProviderId == "cursor");

        Assert.True(codex.HasData);
        Assert.True(codex.HasCostData);
        Assert.False(codex.HasUnpricedData);
        Assert.Equal(1.50m, codex.CostUsd);
        Assert.True(claude.HasCostData);
        Assert.Equal(0.40m, claude.CostUsd);
        Assert.True(grok.HasData);
        Assert.False(grok.HasCostData);
        Assert.True(grok.HasUnpricedData);
        Assert.Equal("—", grok.CostText);
        Assert.False(cursor.HasData);
        Assert.Equal("codex", projection.SelectedProviderId);
        Assert.Same(projection.SelectedProviderLimits, projection.GlobalProviderLimits);
        Assert.NotEmpty(projection.GlobalProviderLimits);
        // Codex and ZCode each read once through the per-provider cache.
        Assert.Equal(2, providerLimitReads);
    }

    [Fact]
    public void ZcodeQuotaWindowsJoinTheGlobalLimitsStripWithAProviderPrefix()
    {
        var today = new DateOnly(2026, 8, 26);

        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            today,
            [],
            ["zcode"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key == "ZcodeGlobalLimitTitlePrefix" ? "ZCode · " : key,
            getProviderLimits: id => id == "zcode"
                ?
                [
                    new QuotaWindow("5-hour credits (estimated)", 75, "75%", "rolling", "ZCode 5h", false, LayoutMetricId: "quota.primary"),
                    new QuotaWindow("Weekly credits (estimated)", 40, "40%", "weekly", "ZCode weekly", false, LayoutMetricId: "quota.secondary"),
                ]
                : []);

        Assert.Collection(
            projection.GlobalProviderLimits,
            fiveHour => Assert.Equal("ZCode · 5-hour credits (estimated)", fiveHour.Title),
            weekly => Assert.Equal("ZCode · Weekly credits (estimated)", weekly.Title));
    }

    [Fact]
    public void GlobalCostBreakdownSplitsReportedAndEstimatedDollars()
    {
        var today = new DateOnly(2026, 8, 13);
        DailyUsageRollup[] rollups =
        [
            Rollup("grok", today, reported: 2m, tokens: 100),
            Rollup("codex", today, estimated: 8m, tokens: 1_000),
        ];

        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            today,
            rollups,
            ["grok", "codex"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key switch
            {
                "CompactGlobalCostBreakdownFormat" => "{0} reported · {1} estimated at API list",
                "LocalUsageUsdFormat" => "${0:N2} USD",
                _ => key,
            },
            getProviderLimits: _ => []);

        Assert.NotNull(projection.GlobalCostBreakdownText);
        string reported = $"{2m:N2} USD reported";
        string estimated = $"{8m:N2} USD estimated";
        Assert.StartsWith(
            "$" + reported,
            projection.GlobalCostBreakdownText,
            StringComparison.Ordinal);
        Assert.Contains(
            "$" + estimated,
            projection.GlobalCostBreakdownText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AllReportedOrAllEstimatedWindowsStillShowBothBreakdownParts()
    {
        var today = new DateOnly(2026, 8, 13);

        CompactDashboardProjection allReported = CompactDashboardProjector.Create(
            today,
            [Rollup("grok", today, reported: 3m, tokens: 100)],
            ["grok"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key switch
            {
                "CompactGlobalCostBreakdownFormat" => "{0} reported · {1} estimated at API list",
                "LocalUsageUsdFormat" => "${0:N2} USD",
                _ => key,
            },
            getProviderLimits: _ => []);
        CompactDashboardProjection allEstimated = CompactDashboardProjector.Create(
            today,
            [Rollup("codex", today, estimated: 4m, tokens: 100)],
            ["codex"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key switch
            {
                "CompactGlobalCostBreakdownFormat" => "{0} reported · {1} estimated at API list",
                "LocalUsageUsdFormat" => "${0:N2} USD",
                _ => key,
            },
            getProviderLimits: _ => []);

        Assert.NotNull(allReported.GlobalCostBreakdownText);
        Assert.Contains(
            $"{0m:N2} USD estimated",
            allReported.GlobalCostBreakdownText,
            StringComparison.Ordinal);
        Assert.NotNull(allEstimated.GlobalCostBreakdownText);
        Assert.Contains(
            $"{0m:N2} USD reported",
            allEstimated.GlobalCostBreakdownText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CostlessWindowsHideTheGlobalCostBreakdown()
    {
        var today = new DateOnly(2026, 8, 13);

        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            today,
            [Rollup("grok", today, tokens: 50, unpriced: 50, coverage: CoverageKind.Unpriced)],
            ["grok"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key switch
            {
                "CompactGlobalCostBreakdownFormat" => "{0} reported · {1} estimated at API list",
                "LocalUsageUsdFormat" => "${0:N2} USD",
                _ => key,
            },
            getProviderLimits: _ => []);

        Assert.Null(projection.GlobalCostBreakdownText);
    }

    private static LocalUsageCard EmptyLocalUsage() => new(
        "",
        "",
        "",
        "",
        [],
        [],
        new("", "", "", "", [], []),
        []);

    private static DailyUsageRollup Rollup(
        string agentId,
        DateOnly date,
        decimal? reported = null,
        decimal? estimated = null,
        long tokens = 10,
        long unpriced = 0,
        CoverageKind coverage = CoverageKind.Complete) =>
        new(
            date,
            "UTC",
            new AgentId(agentId),
            new ModelProviderId("openai"),
            new ModelId("gpt-test"),
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            reported,
            estimated,
            unpriced,
            0,
            1,
            coverage);
}
