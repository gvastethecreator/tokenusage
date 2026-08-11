using Windows.UI.ViewManagement;

namespace TokenUsage.App.Controls;

internal static class MotionSettings
{
    private static readonly UISettings Settings = new();

    public static readonly TimeSpan QuotaRevealDuration = TimeSpan.FromMilliseconds(360);

    public static readonly TimeSpan DonutRevealDuration = TimeSpan.FromMilliseconds(480);

    public static readonly TimeSpan ViewTransitionDuration = TimeSpan.FromMilliseconds(260);

    public static readonly TimeSpan ReportSwitchExitDuration = TimeSpan.FromMilliseconds(180);

    public static readonly TimeSpan ReportSwitchDuration = TimeSpan.FromMilliseconds(320);

    public const double ReportSwitchMinimumOpacity = 0.08;

    public const double ReportRefreshMinimumOpacity = 0.58;

    public const double ReportSwitchOffset = 12;

    public static readonly TimeSpan ProviderSwitchExitDuration = TimeSpan.FromMilliseconds(160);

    public static readonly TimeSpan ProviderSwitchDuration = TimeSpan.FromMilliseconds(320);

    public const double ProviderSwitchMinimumOpacity = 0.18;

    public static bool AreAnimationsEnabled() => Settings.AnimationsEnabled;
}
