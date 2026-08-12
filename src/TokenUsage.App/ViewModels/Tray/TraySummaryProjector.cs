using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Tray;

public sealed record TrayProviderPreference(
    string ProviderId,
    string Name,
    bool IsVisible,
    bool IsSelected);

public sealed class TrayProviderSummary
{
    public TrayProviderSummary(
        string providerId,
        string providerName,
        string sessionLabel,
        string sessionShortLabel,
        string sessionValue,
        QuotaUsageLevel? sessionLevel,
        string periodLabel,
        string periodShortLabel,
        string periodValue,
        QuotaUsageLevel? periodLevel,
        string automationName)
    {
        ProviderId = providerId;
        ProviderName = providerName;
        SessionLabel = sessionLabel;
        SessionShortLabel = sessionShortLabel;
        SessionValue = sessionValue;
        SessionLevel = sessionLevel;
        PeriodLabel = periodLabel;
        PeriodShortLabel = periodShortLabel;
        PeriodValue = periodValue;
        PeriodLevel = periodLevel;
        AutomationName = automationName;
    }

    public string ProviderId { get; set; }

    public string ProviderName { get; set; }

    public string SessionLabel { get; set; }

    public string SessionShortLabel { get; set; }

    public string SessionValue { get; set; }

    public QuotaUsageLevel? SessionLevel { get; set; }

    public string PeriodLabel { get; set; }

    public string PeriodShortLabel { get; set; }

    public string PeriodValue { get; set; }

    public QuotaUsageLevel? PeriodLevel { get; set; }

    public string AutomationName { get; set; }
}

public static class TraySummaryProjector
{
    private static readonly TrayProviderPreference[] DefaultProviders =
    [
        new("codex", "Codex", true, false),
        new("claude", "Claude", true, false),
        new("cursor", "Cursor", true, false),
        new("opencode", "OpenCode", true, false),
    ];

    public static IReadOnlyList<TrayProviderSummary> Create(
        IReadOnlyList<TrayProviderPreference> preferences,
        IReadOnlyList<DashboardProviderSummary> usageSummaries,
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(usageSummaries);
        ArgumentNullException.ThrowIfNull(getProviderLimits);
        ArgumentNullException.ThrowIfNull(getString);

        TrayProviderPreference[] selected = SelectProviders(preferences, usageSummaries);
        return selected
            .Select(preference => CreateProvider(
                preference,
                getProviderLimits(preference.ProviderId),
                getString))
            .ToArray();
    }

    private static TrayProviderPreference[] SelectProviders(
        IReadOnlyList<TrayProviderPreference> preferences,
        IReadOnlyList<DashboardProviderSummary> usageSummaries)
    {
        IEnumerable<TrayProviderPreference> candidates = preferences
            .Where(preference => preference.IsSelected);
        if (!candidates.Any())
        {
            candidates = preferences.Where(preference => preference.IsVisible);
        }

        if (!candidates.Any())
        {
            candidates = usageSummaries.Select(summary => new TrayProviderPreference(
                summary.ProviderId,
                summary.Name,
                IsVisible: true,
                IsSelected: false));
        }

        if (!candidates.Any())
        {
            candidates = DefaultProviders;
        }

        return candidates
            .GroupBy(preference => preference.ProviderId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(DashboardLayout.MaxHighlightedProviders)
            .ToArray();
    }

    private static TrayProviderSummary CreateProvider(
        TrayProviderPreference preference,
        IReadOnlyList<QuotaWindow> limits,
        Func<string, string> getString)
    {
        limits ??= [];
        QuotaWindow? session = FindSession(limits, getString("TraySummarySessionLabel"));
        QuotaWindow? period = FindPeriod(limits, session, getString);
        string unavailable = getString("TraySummaryUnavailableValue");
        string sessionValue = FormatRemaining(session, unavailable);
        string periodValue = FormatRemaining(period, unavailable);
        string sessionLabel = session?.Title ?? getString("TraySummarySessionLabel");
        string periodLabel = period?.Title ?? getString("TraySummaryPeriodLabel");
        string sessionShort = getString("TraySummarySessionShortLabel");
        string periodShort = ResolvePeriodShortLabel(period, getString);
        string automationName = string.Format(
            CultureInfo.CurrentCulture,
            getString("TraySummaryProviderAutomationNameFormat"),
            preference.Name,
            sessionLabel,
            sessionValue,
            periodLabel,
            periodValue);

        return new TrayProviderSummary(
            preference.ProviderId,
            preference.Name,
            sessionLabel,
            sessionShort,
            sessionValue,
            Evaluate(session),
            periodLabel,
            periodShort,
            periodValue,
            Evaluate(period),
            automationName);
    }

    private static QuotaWindow? FindSession(
        IReadOnlyList<QuotaWindow> limits,
        string localizedSessionLabel) =>
        limits.FirstOrDefault(window => string.Equals(
            window.LayoutMetricId,
            "quota.primary",
            StringComparison.Ordinal))
        ?? limits.FirstOrDefault(window => window.LayoutMetricId.Contains(
            "session",
            StringComparison.OrdinalIgnoreCase))
        ?? limits.FirstOrDefault(window => string.Equals(
            window.Title,
            localizedSessionLabel,
            StringComparison.OrdinalIgnoreCase))
        ?? limits.FirstOrDefault(window =>
            window.LayoutMetricId.EndsWith(".primary", StringComparison.Ordinal)
            && !IsAdditionalLimit(window.LayoutMetricId))
        ?? (limits.Count > 0 ? limits[0] : null);

    private static QuotaWindow? FindPeriod(
        IReadOnlyList<QuotaWindow> limits,
        QuotaWindow? session,
        Func<string, string> getString) =>
        limits.FirstOrDefault(window => string.Equals(
            window.LayoutMetricId,
            "quota.secondary",
            StringComparison.Ordinal))
        ?? limits.FirstOrDefault(window =>
            window != session
            && (window.LayoutMetricId.Contains("weekly", StringComparison.OrdinalIgnoreCase)
                || window.LayoutMetricId.Contains("monthly", StringComparison.OrdinalIgnoreCase)))
        ?? limits.FirstOrDefault(window =>
            window != session
            && (string.Equals(
                    window.Title,
                    getString("TraySummaryWeeklyLabel"),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    window.Title,
                    getString("TraySummaryMonthlyLabel"),
                    StringComparison.OrdinalIgnoreCase)))
        ?? limits.FirstOrDefault(window =>
            window != session
            && window.LayoutMetricId.EndsWith(".secondary", StringComparison.Ordinal)
            && !IsAdditionalLimit(window.LayoutMetricId))
        ?? limits.FirstOrDefault(window => window != session);

    private static bool IsAdditionalLimit(string metricId) =>
        metricId.StartsWith("quota.codex-spark.", StringComparison.Ordinal)
        || metricId.StartsWith("quota.codex-bengalfox.", StringComparison.Ordinal)
        || metricId.StartsWith("quota.z-model.", StringComparison.Ordinal);

    private static string ResolvePeriodShortLabel(
        QuotaWindow? period,
        Func<string, string> getString)
    {
        if (period is null)
        {
            return getString("TraySummaryPeriodShortLabel");
        }

        bool monthly = period.LayoutMetricId.Contains(
                "monthly",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                period.Title,
                getString("TraySummaryMonthlyLabel"),
                StringComparison.OrdinalIgnoreCase);
        return getString(monthly
            ? "TraySummaryMonthlyShortLabel"
            : "TraySummaryWeeklyShortLabel");
    }

    private static string FormatRemaining(QuotaWindow? window, string unavailable) =>
        window is null
            ? unavailable
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:0}%",
                Math.Clamp(window.RemainingPercent, 0d, 100d));

    private static QuotaUsageLevel? Evaluate(QuotaWindow? window) =>
        window is null
            ? null
            : QuotaUsageLevelPolicy.Evaluate(
                (decimal)Math.Clamp(window.ColorRemainingPercent, 0d, 100d));
}
