using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.Core.Appearance;
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
            preferences.Select(preference => Usage(preference.ProviderId)).ToArray(),
            _ => [],
            Text);

        Assert.Equal(4, result.Count);
        Assert.Equal(
            ["provider-1", "provider-2", "provider-3", "provider-4"],
            result.Select(item => item.ProviderId));
        Assert.All(result, item =>
        {
            Assert.Equal("—", item.PrimaryValue);
            Assert.Equal("—", item.SecondaryValue);
            Assert.Null(item.PrimaryLevel);
            Assert.Null(item.SecondaryLevel);
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
            [Usage("codex"), Usage("cursor")],
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
        Assert.Equal("80%", codex.PrimaryValue);
        Assert.Equal(QuotaUsageLevel.Healthy, codex.PrimaryLevel);
        Assert.Equal("12%", codex.SecondaryValue);
        Assert.Equal(QuotaUsageLevel.Warning, codex.SecondaryLevel);
        Assert.Equal("W", codex.SecondaryShortLabel);
        Assert.Contains("Weekly: 12%", codex.AutomationName, StringComparison.Ordinal);

        TrayProviderSummary cursor = result[1];
        Assert.Equal("—", cursor.PrimaryValue);
        Assert.Equal("—", cursor.SecondaryValue);
    }

    [Fact]
    public void CreateExcludesProvidersWithoutLocalUsageRows()
    {
        var preferences = new[]
        {
            new TrayProviderPreference("codex", "Codex", true, false),
            new TrayProviderPreference("claude", "Claude", true, false),
        };

        IReadOnlyList<TrayProviderSummary> result = TraySummaryProjector.Create(
            preferences,
            [Usage("codex")],
            _ => [],
            Text);

        Assert.Equal(["codex"], result.Select(item => item.ProviderId));
    }

    [Fact]
    public void CreateReturnsNoProvidersWhenDetectionFoundNoTool()
    {
        var preferences = new[]
        {
            new TrayProviderPreference("codex", "Codex", true, true),
        };

        IReadOnlyList<TrayProviderSummary> result = TraySummaryProjector.Create(
            preferences,
            [],
            _ => [],
            Text);

        Assert.Empty(result);
    }

    [Fact]
    public void CreateProjectsTheConfiguredValuesAndProviderCount()
    {
        var preferences = new[]
        {
            new TrayProviderPreference("codex", "Codex", true, false),
            new TrayProviderPreference("claude", "Claude", true, false),
        };
        var popover = new TrayPopoverSettings(
            TrayPopoverMetric.SpendLast30Days,
            TrayPopoverMetric.TokensLast30Days,
            providerCount: 1,
            showProviderName: true);

        IReadOnlyList<TrayProviderSummary> result = TraySummaryProjector.Create(
            preferences,
            [Usage("codex", costUsd: 42m, tokensText: "1.2M"), Usage("claude")],
            _ => [Window("Session", "quota.primary", 80)],
            Text,
            popover);

        TrayProviderSummary codex = Assert.Single(result);
        Assert.Equal("codex", codex.ProviderId);
        Assert.True(codex.IsProviderNameVisible);
        Assert.Equal("$42", codex.PrimaryValue);
        Assert.Equal("1.2M", codex.SecondaryValue);
        Assert.Null(codex.PrimaryLevel);
        Assert.Null(codex.SecondaryLevel);
    }

    [Fact]
    public void CreateOmitsTheSecondValueWhenTheUserTurnsItOff()
    {
        var preferences = new[]
        {
            new TrayProviderPreference("codex", "Codex", true, false),
        };
        var popover = new TrayPopoverSettings(
            TrayPopoverMetric.SessionQuota,
            TrayPopoverMetric.None,
            providerCount: TrayPopoverSettings.MaxProviderCount,
            showProviderName: false);

        TrayProviderSummary codex = Assert.Single(TraySummaryProjector.Create(
            preferences,
            [Usage("codex")],
            _ => [Window("Session", "quota.primary", 80)],
            Text,
            popover));

        Assert.False(codex.HasSecondaryValue);
        Assert.Equal("80%", codex.PrimaryValue);
        Assert.Equal("Codex. Session: 80%.", codex.AutomationName);
    }

    private static DashboardProviderSummary Usage(
        string providerId,
        decimal costUsd = 0m,
        string tokensText = "—") => new(
        providerId,
        providerId,
        costUsd,
        TotalTokens: 0,
        SharePercent: 0,
        CostText: "—",
        TokensText: tokensText,
        DetailText: "—",
        AccessibleName: providerId,
        ColorHex: "#10A37F",
        AutomationId: $"CompactProvider.{providerId}",
        CompositionWidth: 0,
        HasData: costUsd > 0m || tokensText != "—",
        HasCostData: costUsd > 0m);

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
        "TraySummarySpendLabel" => "Spend, last 30 days",
        "TraySummarySpendShortLabel" => "$",
        "TraySummaryTokensLabel" => "Tokens, last 30 days",
        "TraySummaryTokensShortLabel" => "T",
        "LocalUsageUsdCompactFormat" => "${0:N0}",
        "TraySummaryProviderAutomationNameFormat" => "{0}. {1}: {2}. {3}: {4}.",
        "TraySummaryProviderSingleAutomationNameFormat" => "{0}. {1}: {2}.",
        _ => throw new KeyNotFoundException(key),
    };
}
