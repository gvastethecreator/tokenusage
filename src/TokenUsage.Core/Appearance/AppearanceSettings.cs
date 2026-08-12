namespace TokenUsage.Core.Appearance;

/// <summary>
/// Application theme preference relative to the OS setting.
/// </summary>
public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Spacing density for UI chrome and content.
/// </summary>
public enum AppDensityMode
{
    Regular,
    Compact,
}

/// <summary>
/// Whether usage meters emphasize remaining or used amounts.
/// </summary>
public enum UsageDisplayMode
{
    Remaining,
    Used,
}

/// <summary>
/// How quota reset times are presented to the user.
/// </summary>
public enum ResetTimeDisplayMode
{
    Relative,
    Exact,
}

/// <summary>
/// The single visualization shown on the global compact dashboard.
/// </summary>
public enum DashboardVisualizationMode
{
    List,
    Donut,
    Heatmap,
}

/// <summary>
/// Immutable user-facing appearance preferences.
/// </summary>
public sealed record AppearanceSettings
{
    /// <summary>
    /// Default appearance: system theme, regular density, transparency off,
    /// remaining usage, relative reset times, and the default tray popover.
    /// </summary>
    public static AppearanceSettings Default { get; } = new(
        AppThemeMode.System,
        AppDensityMode.Regular,
        increaseTransparency: false,
        UsageDisplayMode.Remaining,
        ResetTimeDisplayMode.Relative,
        DashboardVisualizationMode.List);

    public AppearanceSettings(
        AppThemeMode theme,
        AppDensityMode density,
        bool increaseTransparency,
        UsageDisplayMode usageDisplay,
        ResetTimeDisplayMode resetTimeDisplay,
        DashboardVisualizationMode dashboardVisualization = DashboardVisualizationMode.List,
        TrayPopoverSettings? trayPopover = null)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, null);
        }

        if (!Enum.IsDefined(density))
        {
            throw new ArgumentOutOfRangeException(nameof(density), density, null);
        }

        if (!Enum.IsDefined(usageDisplay))
        {
            throw new ArgumentOutOfRangeException(nameof(usageDisplay), usageDisplay, null);
        }

        if (!Enum.IsDefined(resetTimeDisplay))
        {
            throw new ArgumentOutOfRangeException(nameof(resetTimeDisplay), resetTimeDisplay, null);
        }

        if (!Enum.IsDefined(dashboardVisualization))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dashboardVisualization),
                dashboardVisualization,
                null);
        }

        Theme = theme;
        Density = density;
        IncreaseTransparency = increaseTransparency;
        UsageDisplay = usageDisplay;
        ResetTimeDisplay = resetTimeDisplay;
        DashboardVisualization = dashboardVisualization;
        TrayPopover = trayPopover ?? TrayPopoverSettings.Default;
    }

    public AppThemeMode Theme { get; }

    public AppDensityMode Density { get; }

    public bool IncreaseTransparency { get; }

    public UsageDisplayMode UsageDisplay { get; }

    public ResetTimeDisplayMode ResetTimeDisplay { get; }

    public DashboardVisualizationMode DashboardVisualization { get; }

    public TrayPopoverSettings TrayPopover { get; }
}
