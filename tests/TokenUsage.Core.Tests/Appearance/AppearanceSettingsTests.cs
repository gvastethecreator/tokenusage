using TokenUsage.Core.Appearance;

namespace TokenUsage.Core.Tests.Appearance;

public sealed class AppearanceSettingsTests
{
    [Fact]
    public void DefaultsMatchTheProductContract()
    {
        AppearanceSettings settings = AppearanceSettings.Default;

        Assert.Equal(AppThemeMode.System, settings.Theme);
        Assert.Equal(AppDensityMode.Regular, settings.Density);
        Assert.False(settings.IncreaseTransparency);
        Assert.Equal(UsageDisplayMode.Remaining, settings.UsageDisplay);
        Assert.Equal(ResetTimeDisplayMode.Relative, settings.ResetTimeDisplay);
        Assert.Equal(DashboardVisualizationMode.List, settings.DashboardVisualization);
        Assert.Equal(TrayPopoverSettings.Default, settings.TrayPopover);
    }

    [Fact]
    public void TrayPopoverDefaultsShowQuotaForEveryHighlightedProvider()
    {
        TrayPopoverSettings popover = TrayPopoverSettings.Default;

        Assert.True(popover.IsEnabled);
        Assert.Equal(TrayPopoverMetric.SessionQuota, popover.PrimaryMetric);
        Assert.Equal(TrayPopoverMetric.PeriodQuota, popover.SecondaryMetric);
        Assert.True(popover.HasSecondaryMetric);
        Assert.Equal(TrayPopoverSettings.MaxProviderCount, popover.ProviderCount);
        Assert.False(popover.ShowProviderName);
    }

    [Fact]
    public void DisabledTrayPopoverKeepsEveryChoiceForLaterReenabling()
    {
        var popover = new TrayPopoverSettings(
            TrayPopoverMetric.SpendLast30Days,
            TrayPopoverMetric.TokensLast30Days,
            providerCount: 2,
            showProviderName: true,
            isEnabled: false);

        Assert.False(popover.IsEnabled);
        Assert.Equal(TrayPopoverMetric.SpendLast30Days, popover.PrimaryMetric);
        Assert.Equal(TrayPopoverMetric.TokensLast30Days, popover.SecondaryMetric);
        Assert.Equal(2, popover.ProviderCount);
        Assert.True(popover.ShowProviderName);
    }

    [Fact]
    public void TrayPopoverDropsADuplicateSecondValueInsteadOfRepeatingIt()
    {
        var popover = new TrayPopoverSettings(
            TrayPopoverMetric.SessionQuota,
            TrayPopoverMetric.SessionQuota,
            providerCount: 1,
            showProviderName: true);

        Assert.Equal(TrayPopoverMetric.None, popover.SecondaryMetric);
        Assert.False(popover.HasSecondaryMetric);
    }

    [Fact]
    public void TrayPopoverRejectsAnEmptyFirstLineAndAnOutOfRangeCount()
    {
        Assert.Equal("primaryMetric", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrayPopoverSettings(
                TrayPopoverMetric.None,
                TrayPopoverMetric.PeriodQuota,
                1,
                false)).ParamName);
        Assert.Equal("primaryMetric", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrayPopoverSettings(
                (TrayPopoverMetric)99,
                TrayPopoverMetric.PeriodQuota,
                1,
                false)).ParamName);
        Assert.Equal("secondaryMetric", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrayPopoverSettings(
                TrayPopoverMetric.SessionQuota,
                (TrayPopoverMetric)99,
                1,
                false)).ParamName);
        Assert.Equal("providerCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrayPopoverSettings(
                TrayPopoverMetric.SessionQuota,
                TrayPopoverMetric.PeriodQuota,
                TrayPopoverSettings.MinProviderCount - 1,
                false)).ParamName);
        Assert.Equal("providerCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrayPopoverSettings(
                TrayPopoverMetric.SessionQuota,
                TrayPopoverMetric.PeriodQuota,
                TrayPopoverSettings.MaxProviderCount + 1,
                false)).ParamName);
    }

    [Fact]
    public void ConstructorRejectsUndefinedEnumValues()
    {
        Assert.Equal("theme", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(theme: (AppThemeMode)99)).ParamName);
        Assert.Equal("density", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(density: (AppDensityMode)99)).ParamName);
        Assert.Equal("usageDisplay", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(usageDisplay: (UsageDisplayMode)99)).ParamName);
        Assert.Equal("resetTimeDisplay", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(resetTimeDisplay: (ResetTimeDisplayMode)99)).ParamName);
        Assert.Equal("dashboardVisualization", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(dashboardVisualization: (DashboardVisualizationMode)99)).ParamName);
    }

    private static AppearanceSettings Create(
        AppThemeMode theme = AppThemeMode.System,
        AppDensityMode density = AppDensityMode.Regular,
        UsageDisplayMode usageDisplay = UsageDisplayMode.Remaining,
        ResetTimeDisplayMode resetTimeDisplay = ResetTimeDisplayMode.Relative,
        DashboardVisualizationMode dashboardVisualization = DashboardVisualizationMode.List) =>
        new(theme, density, false, usageDisplay, resetTimeDisplay, dashboardVisualization);
}
