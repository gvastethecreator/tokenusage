using System.Globalization;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Appearance;

namespace WOpenUsage.App.ViewModels;

public static class AppearanceDashboardProjector
{
    public static SampleDashboardSnapshot Apply(
        SampleDashboardSnapshot dashboard,
        AppearanceSettings settings,
        DateTimeOffset nowUtc,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(getString);

        SampleProviderCard[] providers = dashboard.Providers
            .Select(provider => provider with
            {
                Windows = TransformWindows(provider.Windows, settings, nowUtc, getString),
                SecondaryWindows = TransformWindows(
                    provider.SecondaryWindowItems,
                    settings,
                    nowUtc,
                    getString),
            })
            .ToArray();
        return dashboard with { Providers = providers };
    }

    private static SampleQuotaWindow[] TransformWindows(
        IReadOnlyList<SampleQuotaWindow> windows,
        AppearanceSettings settings,
        DateTimeOffset nowUtc,
        Func<string, string> getString) =>
        windows.Select(window => TransformWindow(window, settings, nowUtc, getString)).ToArray();

    private static SampleQuotaWindow TransformWindow(
        SampleQuotaWindow window,
        AppearanceSettings settings,
        DateTimeOffset nowUtc,
        Func<string, string> getString)
    {
        double remaining = Math.Clamp(window.RemainingPercent, 0d, 100d);
        bool showUsed = settings.UsageDisplay == UsageDisplayMode.Used;
        double displayPercent = showUsed ? 100d - remaining : remaining;
        string usageText = Format(
            getString,
            showUsed ? "AppearanceUsedPercentFormat" : "AppearanceRemainingPercentFormat",
            displayPercent);
        string resetText = FormatReset(window, settings, nowUtc, getString);
        return window with
        {
            RemainingPercent = displayPercent,
            RemainingText = usageText,
            ResetText = resetText,
            AutomationName = $"{window.Title}: {usageText}. {resetText}",
        };
    }

    private static string FormatReset(
        SampleQuotaWindow window,
        AppearanceSettings settings,
        DateTimeOffset nowUtc,
        Func<string, string> getString)
    {
        if (window.ResetAtUtc is not DateTimeOffset resetAtUtc)
        {
            return settings.ResetTimeDisplay == ResetTimeDisplayMode.Exact
                ? getString("AppearanceResetExactUnavailable")
                : window.ResetText;
        }

        if (settings.ResetTimeDisplay == ResetTimeDisplayMode.Exact)
        {
            return Format(
                getString,
                "AppearanceResetExactFormat",
                resetAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }

        TimeSpan remaining = resetAtUtc.ToUniversalTime() - nowUtc.ToUniversalTime();
        if (remaining <= TimeSpan.Zero)
        {
            return getString("CodexResetDue");
        }

        int totalHours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
        int days = totalHours / 24;
        int hours = totalHours % 24;
        return days switch
        {
            0 => Format(getString, "SampleResetHoursFormat", hours),
            _ when hours == 0 => Format(getString, "SampleResetDaysFormat", days),
            _ => Format(getString, "SampleResetDaysHoursFormat", days, hours),
        };
    }

    private static string Format(
        Func<string, string> getString,
        string key,
        params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, getString(key), values);
}
