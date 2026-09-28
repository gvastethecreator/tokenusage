using Windows.UI.ViewManagement;

namespace TokenUsage.App.Controls;

internal static class MotionSettings
{
    private static readonly UISettings Settings = new();

    /// <summary>
    /// True while the report is being captured to an image. Controls that start their own
    /// motion (share bars) settle at their final state instead. UI thread only.
    /// </summary>
    public static bool IsCapturing { get; set; }

    public static readonly TimeSpan QuotaRevealDuration = TimeSpan.FromMilliseconds(360);

    public static readonly TimeSpan DonutRevealDuration = TimeSpan.FromMilliseconds(480);

    public static readonly TimeSpan ViewTransitionDuration = TimeSpan.FromMilliseconds(260);

    public static readonly TimeSpan ReportSwitchExitDuration = TimeSpan.FromMilliseconds(140);

    public static readonly TimeSpan ReportSwitchDuration = TimeSpan.FromMilliseconds(240);

    public const double ReportSwitchMinimumOpacity = 0;

    public const double ReportSwitchOffset = 12;

    public static readonly TimeSpan ProviderSwitchExitDuration = TimeSpan.FromMilliseconds(140);

    public static readonly TimeSpan ProviderSwitchDuration = TimeSpan.FromMilliseconds(240);

    public const double ProviderSwitchMinimumOpacity = 0;

    public static readonly TimeSpan ProviderCarouselDuration = TimeSpan.FromMilliseconds(220);

    public const double ProviderCarouselMinimumOpacity = 0.68;

    public const double ProviderCarouselOffset = 10;

    public static readonly TimeSpan VisualizationSwitchDuration = TimeSpan.FromMilliseconds(280);

    public static readonly TimeSpan ProviderLimitsRevealDuration = TimeSpan.FromMilliseconds(260);

    public static readonly TimeSpan ProviderLimitsFadeDuration = TimeSpan.FromMilliseconds(200);

    public static readonly TimeSpan ChartHoverDuration = TimeSpan.FromMilliseconds(220);

    public static readonly TimeSpan ThemeSwitchDuration = TimeSpan.FromMilliseconds(480);

    public static readonly TimeSpan ReportBarGrowDuration = TimeSpan.FromMilliseconds(460);

    public static readonly TimeSpan ReportChartGrowDuration = TimeSpan.FromMilliseconds(420);

    /// <summary>Per-day delay for the chart column reveal; 30 days finish inside ~0.8 s.</summary>
    public static readonly TimeSpan ReportChartGrowStagger = TimeSpan.FromMilliseconds(12);

    public static bool AreAnimationsEnabled() => Settings.AnimationsEnabled;
}
