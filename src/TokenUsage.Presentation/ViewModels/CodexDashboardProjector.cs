using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.App.ViewModels;

public static class CodexDashboardProjector
{
    private const decimal SessionMaximumMinutes = 12 * 60;
    private const decimal WeeklyMinimumMinutes = 24 * 60;

    public static DashboardSnapshot Create(
        ProviderSnapshot snapshot,
        TimeProvider clock,
        Func<string, string> getString,
        IReadOnlyDictionary<string, long>? windowUsedTokens = null)
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
        Dictionary<string, decimal> scalars = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .ToDictionary(
                metric => metric.Id.Value,
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
        var windows = new List<QuotaWindow>(progressMetrics.Length);
        foreach (ProgressMetricSnapshot metric in progressMetrics)
        {
            windows.Add(CreateWindow(
                metric,
                durations.GetValueOrDefault(metric.Id.Value),
                clock,
                getString,
                windowUsedTokens,
                ref additionalWindow));
        }
        string plan = snapshot.PlanLabel ?? getString("CodexPlanUnknown");
        IReadOnlyList<DashboardMetric> usageMetrics = CreateUsageMetrics(scalars, getString);
        var provider = new ProviderCard(
            "codex",
            "Provider.Codex",
            snapshot.DisplayName,
            plan,
            getString("CodexCapabilityUsage"),
            NoticeText: snapshot.Coverage == CoverageKind.Partial
                ? getString("CodexPartialUsageNotice")
                : null,
            Windows: windows,
            Metrics: usageMetrics,
            SecondaryMetrics: [],
            SourceLabel: getString("ProviderSourceLabel"),
            SourceValue: getString("ProviderSourceOfficialLocalApi"),
            ObservedLabel: getString("ProviderObservedLabel"),
            ObservedValue: Format(
                getString,
                "ProviderObservedValueFormat",
                snapshot.SourceObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            DetailsTooltip: Format(
                getString,
                "ProviderDetailsTooltipFormat",
                getString("ProviderSourceOfficialLocalApi"),
                snapshot.SourceObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            DetailsAutomationName: Format(
                getString,
                "ProviderDetailsAutomationNameFormat",
                snapshot.DisplayName));

        return new DashboardSnapshot(
            SampleScenario.Normal,
            TotalSpendAmount: string.Empty,
            PeriodLabel: getString("CodexQuotaPeriod"),
            SpendAccessibleName: string.Empty,
            SpendSlices: [],
            Providers: [provider]);
    }

    private static QuotaWindow CreateWindow(
        ProgressMetricSnapshot metric,
        decimal durationMinutes,
        TimeProvider clock,
        Func<string, string> text,
        IReadOnlyDictionary<string, long>? windowUsedTokens,
        ref int additionalWindow)
    {
        double remaining = decimal.ToDouble(metric.RemainingPercent);
        double used = decimal.ToDouble(Math.Clamp(
            metric.Used / metric.Limit * 100m,
            0m,
            100m));
        string title = ResolveWindowTitle(
            metric.Id.Value,
            metric.DisplayName,
            metric.LabelEvidence,
            durationMinutes,
            text,
            ref additionalWindow);
        string labelEvidenceText = ResolveLabelEvidenceText(metric.LabelEvidence, text);
        string usage = Format(text, "CodexUsageFormat", remaining, used);
        string reset = FormatReset(metric.ResetsAtUtc, clock, text);
        (string? paceText, bool isPaceBehind) = CreatePace(
            metric,
            durationMinutes,
            clock,
            text);

        string usedTokens = windowUsedTokens is not null
            && windowUsedTokens.TryGetValue(metric.Id.Value, out long usedTokenCount)
                ? Format(
                    text,
                    "CodexQuotaUsedTokensFormat",
                    UsageValueFormatter.CompactTokens(usedTokenCount))
                : string.Empty;

        return new QuotaWindow(
            title,
            remaining,
            usage,
            reset,
            usedTokens.Length == 0
                ? $"Codex, {title}: {usage}. {reset}"
                : $"Codex, {title}: {usage}. {reset}. {usedTokens}",
            remaining <= 15d,
            paceText,
            isPaceBehind,
            $"CodexPace.{metric.Id.Value}",
            LayoutMetricId: metric.Id.Value,
            ResetAtUtc: metric.ResetsAtUtc,
            UsedText: usedTokens,
            LabelEvidenceText: labelEvidenceText);
    }

    private static IReadOnlyList<DashboardMetric> CreateUsageMetrics(
        IReadOnlyDictionary<string, decimal> scalars,
        Func<string, string> text) =>
        [
            CreateUsageMetric("CodexUsageToday", "CodexUsage.Today", CodexUsageMetricIds.Today, scalars, text),
            CreateUsageMetric("CodexUsageYesterday", "CodexUsage.Yesterday", CodexUsageMetricIds.Yesterday, scalars, text),
            CreateUsageMetric("CodexUsageLast7Days", "CodexUsage.Last7Days", CodexUsageMetricIds.Last7Days, scalars, text),
            CreateUsageMetric("CodexUsageLast30Days", "CodexUsage.Last30Days", CodexUsageMetricIds.Last30Days, scalars, text),
        ];

    private static DashboardMetric CreateUsageMetric(
        string labelKey,
        string automationId,
        string metricId,
        IReadOnlyDictionary<string, decimal> scalars,
        Func<string, string> text) =>
        new(
            text(labelKey),
            scalars.TryGetValue(metricId, out decimal value)
                ? Format(
                    text,
                    value == 1m ? "CodexTokenCountSingular" : "CodexTokenCountFormat",
                    value)
                : text("CodexUsageMissing"),
            automationId,
            metricId);

    private static (string? Text, bool IsBehind) CreatePace(
        ProgressMetricSnapshot metric,
        decimal durationMinutes,
        TimeProvider clock,
        Func<string, string> text)
    {
        TimeSpan? duration = CreateDuration(durationMinutes);
        QuotaPaceResult? pace = QuotaPace.Evaluate(
            metric.Used,
            metric.Limit,
            metric.ResetsAtUtc,
            duration,
            clock.GetUtcNow().ToUniversalTime());
        if (pace is null)
        {
            return (null, false);
        }

        decimal projectedPercent = Math.Round(
            pace.ProjectedUsage / metric.Limit * 100m,
            0,
            MidpointRounding.AwayFromZero);
        return pace.Status switch
        {
            QuotaPaceStatus.Ahead => (
                Format(text, "CodexPaceAheadFormat", projectedPercent),
                false),
            QuotaPaceStatus.OnTrack => (
                Format(text, "CodexPaceOnTrackFormat", projectedPercent),
                false),
            _ when pace.TimeToExhaust is TimeSpan eta => (
                Format(
                    text,
                    "CodexPaceBehindEtaFormat",
                    projectedPercent,
                    FormatDuration(eta, text)),
                true),
            _ => (
                Format(text, "CodexPaceBehindFormat", projectedPercent),
                true),
        };
    }

    private static TimeSpan? CreateDuration(decimal durationMinutes)
    {
        if (durationMinutes <= 0m)
        {
            return null;
        }

        double minutes = decimal.ToDouble(durationMinutes);
        if (!double.IsFinite(minutes) || minutes > TimeSpan.MaxValue.TotalMinutes)
        {
            return null;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static string FormatDuration(TimeSpan duration, Func<string, string> text)
    {
        long totalMinutes = Math.Max(1L, checked((long)Math.Ceiling(duration.TotalMinutes)));
        long hours = totalMinutes / 60;
        long minutes = totalMinutes % 60;
        return (hours, minutes) switch
        {
            (0, _) => Format(text, "CodexDurationMinutesFormat", minutes),
            (_, 0) => Format(text, "CodexDurationHoursFormat", hours),
            _ => Format(text, "CodexDurationHoursMinutesFormat", hours, minutes),
        };
    }

    private static string ResolveWindowTitle(
        string metricId,
        string? displayName,
        MetricLabelEvidence? labelEvidence,
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

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        if (!string.IsNullOrWhiteSpace(labelEvidence?.ProviderMetricKey))
        {
            string identity = labelEvidence.ProviderMetricId
                ?? labelEvidence.ProviderMetricKey;
            return durationMinutes > 0
                ? Format(
                    text,
                    "CodexWindowUnnamedDurationFormat",
                    identity,
                    FormatDuration(TimeSpan.FromMinutes(decimal.ToDouble(durationMinutes)), text))
                : Format(text, "CodexWindowUnnamedFormat", identity);
        }

        additionalWindow++;
        return Format(
            text,
            isSecondary
                ? "CodexWindowAdditionalSecondaryFormat"
                : "CodexWindowAdditionalPrimaryFormat",
            additionalWindow);
    }

    private static string ResolveLabelEvidenceText(
        MetricLabelEvidence? evidence,
        Func<string, string> text) => evidence?.Source switch
        {
            MetricLabelSource.Provider => text("CodexLabelProviderSupplied"),
            MetricLabelSource.Duration => text("CodexLabelDurationDerived"),
            MetricLabelSource.Unknown => text("CodexLabelUnknown"),
            _ => string.Empty,
        };

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
