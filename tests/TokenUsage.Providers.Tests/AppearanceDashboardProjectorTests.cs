using System.Globalization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Appearance;

namespace TokenUsage.Providers.Tests;

public sealed class AppearanceDashboardProjectorTests
{
    [Fact]
    public void RemainingModeReformatsPercentAndRefreshesRelativeReset()
    {
        DateTimeOffset now = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        DashboardSnapshot source = Dashboard(Window(23.5, now.AddHours(26)));

        DashboardSnapshot result = AppearanceDashboardProjector.Apply(
            source,
            AppearanceSettings.Default,
            now,
            Text);
        QuotaWindow window = Assert.Single(Assert.Single(result.Providers).Windows);
        string percent = 23.5.ToString("0.#", CultureInfo.CurrentCulture);

        Assert.Equal(23.5, window.RemainingPercent);
        Assert.Equal($"{percent}% left", window.RemainingText);
        Assert.Equal("Resets in 1 d 2 h", window.ResetText);
        Assert.Equal(
            $"Session: {percent}% left. Resets in 1 d 2 h",
            window.AutomationName);
        Assert.Equal("legacy text", Assert.Single(Assert.Single(source.Providers).Windows).RemainingText);
    }

    [Fact]
    public void UsedAndExactModeApplyToPrimaryAndOnDemandWindows()
    {
        DateTimeOffset now = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset reset = now.AddHours(4);
        QuotaWindow primary = Window(20, reset);
        QuotaWindow secondary = Window(65, reset) with { Title = "Weekly" };
        ProviderCard provider = Assert.Single(Dashboard(primary).Providers) with
        {
            SecondaryWindows = [secondary],
        };
        DashboardSnapshot source = Dashboard(primary) with { Providers = [provider] };
        var settings = new AppearanceSettings(
            AppThemeMode.Dark,
            AppDensityMode.Compact,
            true,
            UsageDisplayMode.Used,
            ResetTimeDisplayMode.Exact);

        ProviderCard result = Assert.Single(AppearanceDashboardProjector.Apply(
            source,
            settings,
            now,
            Text).Providers);

        Assert.Equal(80, Assert.Single(result.Windows).RemainingPercent);
        Assert.Equal(20, Assert.Single(result.Windows).ColorRemainingPercent);
        Assert.Equal("80% used", Assert.Single(result.Windows).RemainingText);
        Assert.Equal(35, Assert.Single(result.SecondaryWindowItems).RemainingPercent);
        Assert.Equal(65, Assert.Single(result.SecondaryWindowItems).ColorRemainingPercent);
        Assert.Equal("35% used", Assert.Single(result.SecondaryWindowItems).RemainingText);
        string expectedReset = $"Resets {reset.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        Assert.Equal(expectedReset, Assert.Single(result.Windows).ResetText);
    }

    [Fact]
    public void MissingResetTimestampExplainsExactTimeIsUnavailable()
    {
        QuotaWindow sourceWindow = Window(40, resetAtUtc: null) with
        {
            ResetText = "Provider cycle",
        };
        var exact = new AppearanceSettings(
            AppThemeMode.System,
            AppDensityMode.Regular,
            false,
            UsageDisplayMode.Remaining,
            ResetTimeDisplayMode.Exact);

        QuotaWindow result = Assert.Single(Assert.Single(
            AppearanceDashboardProjector.Apply(
                Dashboard(sourceWindow),
                exact,
                DateTimeOffset.UtcNow,
                Text).Providers).Windows);

        Assert.Equal("Exact reset unavailable", result.ResetText);
    }

    [Fact]
    public void MissingResetTimestampPreservesProviderTextInRelativeMode()
    {
        QuotaWindow sourceWindow = Window(40, resetAtUtc: null) with
        {
            ResetText = "Provider cycle",
        };

        QuotaWindow result = Assert.Single(Assert.Single(
            AppearanceDashboardProjector.Apply(
                Dashboard(sourceWindow),
                AppearanceSettings.Default,
                DateTimeOffset.UtcNow,
                Text).Providers).Windows);

        Assert.Equal("Provider cycle", result.ResetText);
    }

    private static QuotaWindow Window(double remaining, DateTimeOffset? resetAtUtc) => new(
        "Session",
        remaining,
        "legacy text",
        "legacy reset",
        "legacy automation",
        IsNearLimit: remaining <= 15,
        LayoutMetricId: "quota.session",
        ResetAtUtc: resetAtUtc);

    private static DashboardSnapshot Dashboard(QuotaWindow window) => new(
        SampleScenario.Normal,
        "",
        "",
        "",
        [],
        [
            new ProviderCard(
                "codex",
                "Provider.Codex",
                "Codex",
                "Plus",
                "Quota",
                null,
                [window],
                []),
        ]);

    private static string Text(string key) => key switch
    {
        "AppearanceRemainingPercentFormat" => "{0:0.#}% left",
        "AppearanceUsedPercentFormat" => "{0:0.#}% used",
        "AppearanceResetExactFormat" => "Resets {0}",
        "AppearanceResetExactUnavailable" => "Exact reset unavailable",
        "SampleResetHoursFormat" => "Resets in {0} h",
        "SampleResetDaysFormat" => "Resets in {0} d",
        "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
        "CodexResetDue" => "Reset due",
        _ => throw new KeyNotFoundException(key),
    };
}
