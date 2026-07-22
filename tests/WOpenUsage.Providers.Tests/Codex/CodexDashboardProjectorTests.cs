using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Providers.Tests.Codex;

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

        SampleDashboardSnapshot dashboard = CodexDashboardProjector.Create(
            mapped.Snapshot,
            new FixedTimeProvider(Now),
            GetString);

        Assert.False(dashboard.HasSpend);
        Assert.Empty(dashboard.SpendSlices);
        Assert.Equal(string.Empty, dashboard.TotalSpendAmount);
        SampleProviderCard card = Assert.Single(dashboard.Providers);
        Assert.Equal("Codex", card.Name);
        Assert.Equal("Plus", card.PlanLabel);
        Assert.Equal(3, card.Windows.Count);
        Assert.Equal("Session", card.Windows[0].Title);
        Assert.Equal("Weekly", card.Windows[1].Title);
        Assert.Equal("Additional limit 1", card.Windows[2].Title);
        Assert.Equal("58% remaining · 42% used", card.Windows[0].RemainingText);
        Assert.Equal("Resets in 4 h", card.Windows[0].ResetText);

        string rendered = string.Join(
            '\n',
            card.Windows.Select(window => $"{window.Title}|{window.RemainingText}|{window.AutomationName}"));
        Assert.DoesNotContain("model-private-name", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("email", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", rendered, StringComparison.OrdinalIgnoreCase);
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

    private static string GetString(string key) => key switch
    {
        "CodexQuotaPeriod" => "Current Codex limits",
        "CodexPlanUnknown" => "Plan unavailable",
        "CodexUsageFormat" => "{0}% remaining · {1}% used",
        "CodexWindowPrimary" => "Primary limit",
        "CodexWindowSecondary" => "Secondary limit",
        "CodexWindowAdditionalPrimaryFormat" => "Additional limit {0}",
        "CodexWindowAdditionalSecondaryFormat" => "Additional limit {0}",
        "CodexResetUnknown" => "Reset time unavailable",
        "CodexResetDue" => "Reset due",
        "SampleWindowSession" => "Session",
        "SampleWindowWeekly" => "Weekly",
        "SampleResetHoursFormat" => "Resets in {0} h",
        "SampleResetDaysFormat" => "Resets in {0} d",
        "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
        "SampleCapabilityQuota" => "Quota",
        _ => throw new InvalidOperationException($"Unexpected resource '{key}'."),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
