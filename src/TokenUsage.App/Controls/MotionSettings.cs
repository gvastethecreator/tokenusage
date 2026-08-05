using Windows.UI.ViewManagement;

namespace TokenUsage.App.Controls;

internal static class MotionSettings
{
    private static readonly UISettings Settings = new();

    public static readonly TimeSpan QuotaRevealDuration = TimeSpan.FromMilliseconds(360);

    public static readonly TimeSpan DonutRevealDuration = TimeSpan.FromMilliseconds(480);

    public static readonly TimeSpan ViewTransitionDuration = TimeSpan.FromMilliseconds(200);

    public static bool AreAnimationsEnabled() => Settings.AnimationsEnabled;
}
