using System.Globalization;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels;

public static class CodexDashboardProjector
{
    private const decimal SessionMaximumMinutes = 12 * 60;
    private const decimal WeeklyMinimumMinutes = 24 * 60;

    public static SampleDashboardSnapshot Create(
        ProviderSnapshot snapshot,
        TimeProvider clock,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(snapshot.ProviderId.Value, "codex", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Codex dashboard requires the Codex provider ID.",
                nameof(snapshot));
        }

        Dictionary<string, decimal> durations = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .Where(metric => metric.Id.Value.EndsWith(".window-minutes", StringComparison.Ordinal))
            .ToDictionary(
                metric => metric.Id.Value[..^".window-minutes".Length],
                metric => metric.Value,
                StringComparer.Ordinal);

        ProgressMetricSnapshot[] progressMetrics = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .ToArray();
        if (progressMetrics.Length == 0)
        {
            throw new ArgumentException(
                "The Codex dashboard requires at least one quota window.",
                nameof(snapshot));
        }

        var additionalWindow = 0;
        var windows = new List<SampleQuotaWindow>(progressMetrics.Length);
        foreach (ProgressMetricSnapshot metric in progressMetrics)
        {
            windows.Add(CreateWindow(
                metric,
                durations.GetValueOrDefault(metric.Id.Value),
                clock,
                getString,
                ref additionalWindow));
        }
        string plan = snapshot.PlanLabel ?? getString("CodexPlanUnknown");
        var provider = new SampleProviderCard(
            "codex",
            "Provider.Codex",
            snapshot.DisplayName,
            plan,
            getString("SampleCapabilityQuota"),
            NoticeText: null,
            Windows: windows,
            Metrics: []);

        return new SampleDashboardSnapshot(
            SampleScenario.Normal,
            TotalSpendAmount: string.Empty,
            PeriodLabel: getString("CodexQuotaPeriod"),
            SpendAccessibleName: string.Empty,
            SpendSlices: [],
            Providers: [provider]);
    }

    private static SampleQuotaWindow CreateWindow(
        ProgressMetricSnapshot metric,
        decimal durationMinutes,
        TimeProvider clock,
        Func<string, string> text,
        ref int additionalWindow)
    {
        double remaining = decimal.ToDouble(metric.RemainingPercent);
        double used = decimal.ToDouble(Math.Clamp(
            metric.Used / metric.Limit * 100m,
            0m,
            100m));
        string title = ResolveWindowTitle(
            metric.Id.Value,
            durationMinutes,
            text,
            ref additionalWindow);
        string usage = Format(text, "CodexUsageFormat", remaining, used);
        string reset = FormatReset(metric.ResetsAtUtc, clock, text);

        return new SampleQuotaWindow(
            title,
            remaining,
            usage,
            reset,
            $"Codex, {title}: {usage}. {reset}",
            remaining <= 15d);
    }

    private static string ResolveWindowTitle(
        string metricId,
        decimal durationMinutes,
        Func<string, string> text,
        ref int additionalWindow)
    {
        bool isPrimary = metricId.EndsWith(".primary", StringComparison.Ordinal);
        bool isSecondary = metricId.EndsWith(".secondary", StringComparison.Ordinal);
        bool isBaseQuota = metricId.StartsWith("quota.", StringComparison.Ordinal)
            && metricId.Count(character => character == '.') == 1;

        if (isBaseQuota && durationMinutes is > 0 and <= SessionMaximumMinutes)
        {
            return text("SampleWindowSession");
        }

        if (isBaseQuota && durationMinutes >= WeeklyMinimumMinutes)
        {
            return text("SampleWindowWeekly");
        }

        if (isBaseQuota)
        {
            return text(isPrimary ? "CodexWindowPrimary" : "CodexWindowSecondary");
        }

        additionalWindow++;
        return Format(
            text,
            isSecondary
                ? "CodexWindowAdditionalSecondaryFormat"
                : "CodexWindowAdditionalPrimaryFormat",
            additionalWindow);
    }

    private static string FormatReset(
        DateTimeOffset? resetsAtUtc,
        TimeProvider clock,
        Func<string, string> text)
    {
        if (resetsAtUtc is null)
        {
            return text("CodexResetUnknown");
        }

        TimeSpan remaining = resetsAtUtc.Value - clock.GetUtcNow().ToUniversalTime();
        if (remaining <= TimeSpan.Zero)
        {
            return text("CodexResetDue");
        }

        int totalHours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
        int days = totalHours / 24;
        int hours = totalHours % 24;
        return days switch
        {
            0 => Format(text, "SampleResetHoursFormat", hours),
            _ when hours == 0 => Format(text, "SampleResetDaysFormat", days),
            _ => Format(text, "SampleResetDaysHoursFormat", days, hours),
        };
    }

    private static string Format(
        Func<string, string> text,
        string key,
        params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, text(key), args);
}
