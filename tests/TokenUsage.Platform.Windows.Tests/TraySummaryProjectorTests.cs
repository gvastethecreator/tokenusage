using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.Core.Usage;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class TraySummaryProjectorTests
{
    [Fact]
    public void CreateUsesSelectedProvidersInOrderAndCapsTheStripAtFour()
    {
        TrayProviderPreference[] preferences = Enumerable.Range(0, 6)
            .Select(index => new TrayProviderPreference(
                $"provider-{index}",
                $"Provider {index}",
                IsVisible: true,
                IsSelected: index > 0))
            .ToArray();

        IReadOnlyList<TrayProviderSummary> result = TraySummaryProjector.Create(
            preferences,
            [],
            _ => [],
            Text);

        Assert.Equal(4, result.Count);
        Assert.Equal(
            ["provider-1", "provider-2", "provider-3", "provider-4"],
            result.Select(item => item.ProviderId));
        Assert.All(result, item =>
        {
            Assert.Equal("—", item.SessionValue);
            Assert.Equal("—", item.PeriodValue);
            Assert.Null(item.SessionLevel);
            Assert.Null(item.PeriodLevel);
        });
    }

    [Fact]
    public void CreateProjectsObservedLimitsAndKeepsMissingValuesUnavailable()
    {
        var preferences = new[]
        {
            new TrayProviderPreference("codex", "Codex", true, true),
            new TrayProviderPreference("cursor", "Cursor", true, true),
        };

        IReadOnlyList<TrayProviderSummary> result = TraySummaryProjector.Create(
            preferences,
            [],
            providerId => providerId == "codex"
                ?
                [
                    Window("Session", "quota.primary", 80),
                    Window("Weekly", "quota.secondary", 12),
                    Window("Codex Spark", "quota.codex-spark.primary", 100),
                ]
                : [],
            Text);

        TrayProviderSummary codex = result[0];
        Assert.Equal("80%", codex.SessionValue);
        Assert.Equal(QuotaUsageLevel.Healthy, codex.SessionLevel);
        Assert.Equal("12%", codex.PeriodValue);
        Assert.Equal(QuotaUsageLevel.Warning, codex.PeriodLevel);
        Assert.Equal("W", codex.PeriodShortLabel);
        Assert.Contains("Weekly: 12%", codex.AutomationName, StringComparison.Ordinal);

        TrayProviderSummary cursor = result[1];
        Assert.Equal("—", cursor.SessionValue);
        Assert.Equal("—", cursor.PeriodValue);
    }

    private static QuotaWindow Window(string title, string metricId, double remaining) => new(
        title,
        remaining,
        $"{remaining}% remaining",
        "Reset unavailable",
        title,
        IsNearLimit: remaining <= 15,
        LayoutMetricId: metricId);

    private static string Text(string key) => key switch
    {
        "TraySummaryUnavailableValue" => "—",
        "TraySummarySessionLabel" => "Session",
        "TraySummaryWeeklyLabel" => "Weekly",
        "TraySummaryMonthlyLabel" => "Monthly",
        "TraySummaryPeriodLabel" => "Weekly or monthly",
        "TraySummarySessionShortLabel" => "S",
        "TraySummaryWeeklyShortLabel" => "W",
        "TraySummaryMonthlyShortLabel" => "M",
        "TraySummaryPeriodShortLabel" => "W/M",
        "TraySummaryProviderAutomationNameFormat" => "{0}. {1}: {2}. {3}: {4}.",
        _ => throw new KeyNotFoundException(key),
    };
}
