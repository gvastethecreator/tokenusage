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
