using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Reports;
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
            ["codex", "cursor"],
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
        Assert.Equal(1_280, projection.ProviderSummaries.Sum(item => item.TotalTokens));
        Assert.Equal(1.90m, projection.ProviderSummaries.Sum(item => item.CostUsd));
        Assert.Equal("codex", projection.SelectedProviderId);
        Assert.Same(projection.SelectedProviderLimits, projection.GlobalCodexLimits);
        Assert.NotEmpty(projection.GlobalCodexLimits);
        // Codex, Claude, and ZCode each read once through the per-provider cache.
        Assert.Equal(3, providerLimitReads);
    }

    [Fact]
    public void ZcodeQuotaWindowsKeepTheirOwnGlobalGroup()
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
            getString: key => key,
            getProviderLimits: id => id == "zcode"
                ?
                [
                    new QuotaWindow("5-hour credits (estimated)", 75, "75%", "rolling", "ZCode 5h", false, LayoutMetricId: "quota.primary"),
                    new QuotaWindow("Weekly credits (estimated)", 40, "40%", "weekly", "ZCode weekly", false, LayoutMetricId: "quota.secondary"),
                ]
                : []);

        Assert.Collection(
            projection.GlobalZcodeLimits,
            fiveHour => Assert.Equal("5-hour credits (estimated)", fiveHour.Title),
            weekly => Assert.Equal("Weekly credits (estimated)", weekly.Title));
        Assert.Empty(projection.GlobalCodexLimits);
        Assert.Empty(projection.GlobalClaudeLimits);
    }

    [Fact]
    public void ClaudeQuotaWindowsKeepTheirOwnGlobalGroup()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        ProviderSnapshot snapshot = TokenUsage.Providers.Claude.ClaudeRateLimitSnapshotMapper.Map(
            new TokenUsage.Providers.Claude.ClaudeRateLimitSnapshot(
                now,
                [
                    new("seven_day", 41.2m, now.AddDays(3)),
                    new("five_hour", 23.5m, now.AddHours(2)),
                ]),
            now,
            "UTC")!;
        ProviderCard claude = ClaudeDashboardProjector.Create(
            snapshot,
            new FixedClock(now),
            key => key switch
            {
                "SampleWindowSession" => "Session",
                "SampleWindowWeekly" => "Weekly",
                "LocalUsageAgentClaude" => "Claude",
                _ => key,
            },
            new Dictionary<string, long> { ["quota.five-hour"] = 1_500 });

        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            new DateOnly(2026, 9, 24),
            [],
            ["claude"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: null,
            getString: key => key,
            getProviderLimits: id => id switch
            {
                "codex" => [new QuotaWindow("Session", 80, "80%", "soon", "Codex, Session", false)],
                "claude" => claude.Windows,
                _ => [],
            });

        Assert.Null(claude.NoticeText);
        Assert.Equal("Session", Assert.Single(projection.GlobalCodexLimits).Title);
        Assert.Collection(
            projection.GlobalClaudeLimits,
            session =>
            {
                Assert.Equal("Session", session.Title);
                Assert.Equal(76.5d, session.RemainingPercent, precision: 3);
                Assert.StartsWith("Claude, Session", session.AutomationName, StringComparison.Ordinal);
                Assert.NotEmpty(session.UsedText);
            },
            weekly => Assert.Equal("Weekly", weekly.Title));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
                "LocalUsageUsdTinyFormat" => "{0} USD",
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
                "LocalUsageUsdTinyFormat" => "{0} USD",
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
                "LocalUsageUsdTinyFormat" => "{0} USD",
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
                "LocalUsageUsdTinyFormat" => "{0} USD",
                _ => key,
            },
            getProviderLimits: _ => []);

        Assert.Null(projection.GlobalCostBreakdownText);
    }

    [Fact]
    public void MeasuredZeroStaysMeasuredAndDoesNotCarryThePreviousValue()
    {
        var today = new DateOnly(2026, 9, 13);
        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            today,
            [
                Rollup("codex", today.AddDays(-1), tokens: 600),
                Rollup("codex", today, tokens: 0),
            ],
            ["codex"],
            isSampleMode: false,
            activeSample: null,
            EmptyLocalUsage(),
            selectedProviderId: "codex",
            getString: key => key,
            getProviderLimits: _ => []);

        UsageReportTrendSeries series = Assert.Single(projection.SelectedProviderTrend.Series);
        Assert.Equal(30, series.Values.Count);
        Assert.Equal(600, series.Values[28]);
        Assert.Equal(0, series.Values[29]);
        Assert.Equal(UsageTrendPointKind.Unobserved, series.PointKinds[0]);
        Assert.Equal(UsageTrendPointKind.Measured, series.PointKinds[28]);
        Assert.Equal(UsageTrendPointKind.Measured, series.PointKinds[29]);

        UsageTrendPath path = UsageTrendGeometry.CreatePath(
            series.Values,
            400,
            200,
            600,
            style: TokenUsage.Core.Appearance.ReportChartStyle.Smooth,
            pointKinds: series.PointKinds);
        Assert.Equal(path.Points[29], path.Segments[^1].To);
        Assert.False(path.Segments[^1].IsTrailingContinuation);
        Assert.Equal(UsageTrendSpanKind.Observed, path.Segments[^1].SpanKind);
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
