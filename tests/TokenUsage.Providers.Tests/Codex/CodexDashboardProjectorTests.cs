using System.Globalization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexDashboardProjectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RealQuotaCreatesOneCodexCardWithoutSyntheticSpendOrAccountData()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket(
                "plus",
                new CodexRateLimitWindow(42, Now.AddHours(4), 300),
                new CodexRateLimitWindow(18, Now.AddDays(5), 10080)),
            new Dictionary<string, CodexRateLimitBucket>
            {
                ["model-private-name"] = new(
                    "plus",
                    new CodexRateLimitWindow(75, Now.AddHours(1), 60),
                    null),
            });
        CodexSnapshotMappingResult.Available mapped = Assert.IsType<
            CodexSnapshotMappingResult.Available>(
                CodexRateLimitsSnapshotMapper.Map(source, Now, "UTC"));

        DashboardSnapshot dashboard = CodexDashboardProjector.Create(
            mapped.Snapshot,
            new FixedTimeProvider(Now),
            GetString);

        Assert.False(dashboard.HasSpend);
        Assert.Empty(dashboard.SpendSlices);
        Assert.Equal(string.Empty, dashboard.TotalSpendAmount);
        ProviderCard card = Assert.Single(dashboard.Providers);
        Assert.Equal("Codex", card.Name);
        Assert.Equal("Plus", card.PlanLabel);
        Assert.Equal(3, card.Windows.Count);
        Assert.Equal("Session", card.Windows[0].Title);
        Assert.Equal("Weekly", card.Windows[1].Title);
        Assert.Equal("Additional limit 1", card.Windows[2].Title);
        Assert.Equal("58% remaining · 42% used", card.Windows[0].RemainingText);
        Assert.Equal("Resets in 4 h", card.Windows[0].ResetText);
        Assert.Equal("210% projected · limit in 1 h 23 min", card.Windows[0].PaceText);
        Assert.True(card.Windows[0].HasPace);
        Assert.True(card.Windows[0].IsPaceBehind);
        Assert.False(card.Windows[0].IsPaceWithinLimit);
        Assert.Equal("63% projected · below pace", card.Windows[1].PaceText);
        Assert.True(card.Windows[1].HasPace);
        Assert.False(card.Windows[1].IsPaceBehind);
        Assert.True(card.Windows[1].IsPaceWithinLimit);
        Assert.Null(card.NoticeText);
        Assert.False(card.HasNotice);
        Assert.Equal(4, card.Metrics.Count);
        Assert.False(card.HasSecondaryMetrics);
        Assert.True(card.HasDetails);
        Assert.Equal("Source", card.SourceLabel);
        Assert.Equal("Official local API", card.SourceValue);
        Assert.Equal("Updated", card.ObservedLabel);
        Assert.False(string.IsNullOrWhiteSpace(card.ObservedValue));
        Assert.Contains("Official local API", card.DetailsTooltip, StringComparison.Ordinal);
        Assert.Equal("Details for Codex", card.DetailsAutomationName);
        Assert.Collection(
            card.Metrics,
            metric => Assert.Equal(new DashboardMetric("Today", "No data", "CodexUsage.Today", "usage.tokens.today"), metric),
            metric => Assert.Equal(new DashboardMetric("Yesterday", "No data", "CodexUsage.Yesterday", "usage.tokens.yesterday"), metric),
            metric => Assert.Equal(new DashboardMetric("Last 7 days", "No data", "CodexUsage.Last7Days", "usage.tokens.7d"), metric),
            metric => Assert.Equal(new DashboardMetric("Last 30 days", "No data", "CodexUsage.Last30Days", "usage.tokens.30d"), metric));

        string rendered = string.Join('\n',
        [
            .. card.Windows.Select(window =>
                $"{window.Title}|{window.RemainingText}|{window.ResetText}|{window.PaceText}|{window.AutomationName}"),
            .. card.Metrics.Select(metric => $"{metric.Label}|{metric.Value}"),
        ]);
        Assert.DoesNotContain("model-private-name", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("email", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingDurationOrResetHidesPaceWithoutHidingQuota()
    {
        QuotaWindow missingDuration = Assert.Single(CreateCard(
            used: 25m,
            reset: Now.AddHours(3),
            durationMinutes: null).Windows);
        QuotaWindow missingReset = Assert.Single(CreateCard(
            used: 25m,
            reset: null,
            durationMinutes: 300m).Windows);

        Assert.Equal("Resets in 3 h", missingDuration.ResetText);
        Assert.False(missingDuration.HasPace);
        Assert.False(missingDuration.IsPaceBehind);
        Assert.False(missingDuration.IsPaceWithinLimit);
        Assert.Equal("Reset time unavailable", missingReset.ResetText);
        Assert.False(missingReset.HasPace);
        Assert.False(missingReset.IsPaceBehind);
        Assert.False(missingReset.IsPaceWithinLimit);
    }

    [Fact]
    public void WindowUsedTokensSurfaceAsCompactCycleText()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            QuotaWindow withTokens = Assert.Single(CreateCard(
                used: 25m,
                reset: Now.AddHours(3),
                durationMinutes: 300m,
                windowUsedTokens: new Dictionary<string, long>
                {
                    ["quota.primary"] = 1_234_567L,
                }).Windows);
            QuotaWindow withoutTokens = Assert.Single(CreateCard(
                used: 25m,
                reset: Now.AddHours(3),
                durationMinutes: 300m).Windows);

            Assert.Equal("1.2M tokens used", withTokens.UsedText);
            Assert.Contains("1.2M tokens used", withTokens.AutomationName, StringComparison.Ordinal);
            Assert.Equal(string.Empty, withoutTokens.UsedText);
            Assert.DoesNotContain("tokens used", withoutTokens.AutomationName, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void UnknownWindowTokensAreOmittedRatherThanInvented()
    {
        QuotaWindow unknownMetric = Assert.Single(CreateCard(
            used: 25m,
            reset: Now.AddHours(3),
            durationMinutes: 300m,
            windowUsedTokens: new Dictionary<string, long>
            {
                ["quota.someone-else"] = 9_999L,
            }).Windows);

        Assert.Equal(string.Empty, unknownMetric.UsedText);
    }

    [Fact]
    public void OnTrackAndExhaustedPaceMapToDistinctPresentationStates()
    {
        QuotaWindow onTrack = Assert.Single(CreateCard(
            used: 50m,
            reset: Now.AddHours(2.5),
            durationMinutes: 300m).Windows);
        QuotaWindow exhausted = Assert.Single(CreateCard(
            used: 100m,
            reset: Now.AddHours(2.5),
            durationMinutes: 300m).Windows);

        Assert.Equal("100% projected · on pace", onTrack.PaceText);
        Assert.True(onTrack.HasPace);
        Assert.True(onTrack.IsPaceWithinLimit);
        Assert.False(onTrack.IsPaceBehind);
        Assert.Equal("200% projected · above pace", exhausted.PaceText);
        Assert.True(exhausted.HasPace);
        Assert.False(exhausted.IsPaceWithinLimit);
        Assert.True(exhausted.IsPaceBehind);
    }

    [Fact]
    public void DailyUsageDistinguishesObservedZeroFromMissingAndPartialCoverage()
    {
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.Derived,
            "codex-app-server/1");
        var snapshot = new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Plus",
            Now,
            Now,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("quota.primary"),
                    25m,
                    100m,
                    Now.AddHours(3),
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("quota.primary.window-minutes"),
                    300m,
                    "minutes",
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("usage.tokens.today"),
                    0m,
                    "tokens",
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("usage.tokens.7d"),
                    1234567m,
                    "tokens",
                    provenance),
            ],
            CoverageKind.Partial,
            1);

        ProviderCard card = Assert.Single(CodexDashboardProjector.Create(
            snapshot,
            new FixedTimeProvider(Now),
            GetString).Providers);

        Assert.Equal("Daily token usage is incomplete.", card.NoticeText);
        Assert.Empty(card.SecondaryMetricItems);
        Assert.Collection(
            card.Metrics,
            metric => Assert.Equal(new DashboardMetric("Today", "0 tokens", "CodexUsage.Today", "usage.tokens.today"), metric),
            metric => Assert.Equal(new DashboardMetric("Yesterday", "No data", "CodexUsage.Yesterday", "usage.tokens.yesterday"), metric),
            metric => Assert.Equal(
                new DashboardMetric(
                    "Last 7 days",
                    $"{1234567m.ToString("N0", CultureInfo.CurrentCulture)} tokens",
                    "CodexUsage.Last7Days",
                    "usage.tokens.7d"),
                metric),
            metric => Assert.Equal(new DashboardMetric("Last 30 days", "No data", "CodexUsage.Last30Days", "usage.tokens.30d"), metric));
    }

    [Fact]
    public void BengalfoxAdditionalLimitUsesTheCodexSparkProductName()
    {
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            "codex-app-server/1");
        var snapshot = new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Pro",
            Now,
            Now,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("quota.primary"),
                    6m,
                    100m,
                    Now.AddDays(7),
                    provenance),
                new ProgressMetricSnapshot(
                    new MetricId("quota.codex-bengalfox.primary"),
                    0m,
                    100m,
                    Now.AddDays(7),
                    provenance),
            ],
            CoverageKind.Complete,
            1);

        ProviderCard card = Assert.Single(CodexDashboardProjector.Create(
            snapshot,
            new FixedTimeProvider(Now),
            GetString).Providers);

        Assert.Equal("Codex Spark", card.Windows[1].Title);
    }

    [Fact]
    public void SnapshotWithoutQuotaWindowsIsRejected()
    {
        var snapshot = new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Plus",
            Now,
            Now,
            "UTC",
            [],
            CoverageKind.Complete,
            1);

        Assert.Throws<ArgumentException>(() => CodexDashboardProjector.Create(
            snapshot,
            new FixedTimeProvider(Now),
            GetString));
    }

    private static ProviderCard CreateCard(
        decimal used,
        DateTimeOffset? reset,
        decimal? durationMinutes,
        IReadOnlyDictionary<string, long>? windowUsedTokens = null)
    {
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            "codex-app-server/1");
        var metrics = new List<MetricSnapshot>
        {
            new ProgressMetricSnapshot(
                new MetricId("quota.primary"),
                used,
                100m,
                reset,
                provenance),
        };
        if (durationMinutes is decimal duration)
        {
            metrics.Add(new ScalarMetricSnapshot(
                new MetricId("quota.primary.window-minutes"),
                duration,
                "minutes",
                provenance));
        }

        var snapshot = new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Plus",
            Now,
            Now,
            "UTC",
            metrics,
            CoverageKind.Complete,
            1);
        return Assert.Single(CodexDashboardProjector.Create(
            snapshot,
            new FixedTimeProvider(Now),
            GetString,
            windowUsedTokens).Providers);
    }

    private static string GetString(string key) => key switch
    {
        "CodexQuotaPeriod" => "Current Codex limits",
        "CodexPlanUnknown" => "Plan unavailable",
        "CodexUsageFormat" => "{0}% remaining · {1}% used",
        "CodexWindowPrimary" => "Primary limit",
        "CodexWindowSecondary" => "Secondary limit",
        "CodexWindowAdditionalPrimaryFormat" => "Additional limit {0}",
        "CodexWindowAdditionalSecondaryFormat" => "Additional limit {0}",
        "CodexWindowSpark" => "Codex Spark",
        "CodexResetUnknown" => "Reset time unavailable",
        "CodexResetDue" => "Reset due",
        "SampleWindowSession" => "Session",
        "SampleWindowWeekly" => "Weekly",
        "SampleResetHoursFormat" => "Resets in {0} h",
        "SampleResetDaysFormat" => "Resets in {0} d",
        "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
        "SampleCapabilityQuota" => "Quota",
        "CodexPartialUsageNotice" => "Daily token usage is incomplete.",
        "CodexCapabilityUsage" => "Limits and official usage",
        "ProviderSourceLabel" => "Source",
        "ProviderSourceOfficialLocalApi" => "Official local API",
        "ProviderObservedLabel" => "Updated",
        "ProviderObservedValueFormat" => "{0}",
        "ProviderDetailsTooltipFormat" => "Source: {0}. Updated: {1}.",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
        "CodexUsageToday" => "Today",
        "CodexUsageYesterday" => "Yesterday",
        "CodexUsageLast7Days" => "Last 7 days",
        "CodexUsageLast30Days" => "Last 30 days",
        "CodexUsageMissing" => "No data",
        "CodexTokenCountFormat" => "{0:N0} tokens",
        "CodexTokenCountSingular" => "{0:N0} token",
        "CodexQuotaUsedTokensFormat" => "{0} tokens used",
        "CodexPaceAheadFormat" => "{0}% projected · below pace",
        "CodexPaceOnTrackFormat" => "{0}% projected · on pace",
        "CodexPaceBehindFormat" => "{0}% projected · above pace",
        "CodexPaceBehindEtaFormat" => "{0}% projected · limit in {1}",
        "CodexDurationHoursFormat" => "{0} h",
        "CodexDurationMinutesFormat" => "{0} min",
        "CodexDurationHoursMinutesFormat" => "{0} h {1} min",
        _ => throw new InvalidOperationException($"Unexpected resource '{key}'."),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
