using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Appearance;
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
        bool isProviderNameVisible,
        string primaryLabel,
        string primaryShortLabel,
        string primaryValue,
        QuotaUsageLevel? primaryLevel,
        string secondaryLabel,
        string secondaryShortLabel,
        string secondaryValue,
        QuotaUsageLevel? secondaryLevel,
        bool hasSecondaryValue,
        string automationName)
    {
        ProviderId = providerId;
        ProviderName = providerName;
        IsProviderNameVisible = isProviderNameVisible;
        PrimaryLabel = primaryLabel;
        PrimaryShortLabel = primaryShortLabel;
        PrimaryValue = primaryValue;
        PrimaryLevel = primaryLevel;
        SecondaryLabel = secondaryLabel;
        SecondaryShortLabel = secondaryShortLabel;
        SecondaryValue = secondaryValue;
        SecondaryLevel = secondaryLevel;
        HasSecondaryValue = hasSecondaryValue;
        AutomationName = automationName;
    }

    public string ProviderId { get; }

    public string ProviderName { get; }

    public bool IsProviderNameVisible { get; }

    public string PrimaryLabel { get; }

    public string PrimaryShortLabel { get; }

    public string PrimaryValue { get; }

    public QuotaUsageLevel? PrimaryLevel { get; }

    public string SecondaryLabel { get; }

    public string SecondaryShortLabel { get; }

    public string SecondaryValue { get; }

    public QuotaUsageLevel? SecondaryLevel { get; }

    public bool HasSecondaryValue { get; }

    public string AutomationName { get; }
}

public static class TraySummaryProjector
{
    public static IReadOnlyList<TrayProviderSummary> Create(
        IReadOnlyList<TrayProviderPreference> preferences,
        IReadOnlyList<DashboardProviderSummary> usageSummaries,
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits,
        Func<string, string> getString,
        TrayPopoverSettings? popover = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(usageSummaries);
        ArgumentNullException.ThrowIfNull(getProviderLimits);
        ArgumentNullException.ThrowIfNull(getString);
        popover ??= TrayPopoverSettings.Default;

        Dictionary<string, DashboardProviderSummary> usageById = usageSummaries
            .GroupBy(summary => summary.ProviderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return SelectProviders(preferences, usageSummaries, popover.ProviderCount)
            .Select(preference => CreateProvider(
                preference,
                getProviderLimits(preference.ProviderId),
                usageById.GetValueOrDefault(preference.ProviderId),
                popover,
                getString))
            .ToArray();
    }

    /// <summary>
    /// Only providers with local usage rows reach the popover. Those rows already exclude
    /// tools whose local root is missing, so an uninstalled tool never occupies the strip.
    /// </summary>
    private static TrayProviderPreference[] SelectProviders(
        IReadOnlyList<TrayProviderPreference> preferences,
        IReadOnlyList<DashboardProviderSummary> usageSummaries,
        int providerCount)
    {
        HashSet<string> detected = usageSummaries
            .Select(summary => summary.ProviderId)
            .ToHashSet(StringComparer.Ordinal);
        if (detected.Count == 0)
        {
            return [];
        }

        TrayProviderPreference[] allowed = preferences
            .Where(preference => detected.Contains(preference.ProviderId))
            .ToArray();
        if (allowed.Length == 0)
        {
            allowed = usageSummaries
                .Select(summary => new TrayProviderPreference(
                    summary.ProviderId,
                    summary.Name,
                    IsVisible: true,
                    IsSelected: false))
                .ToArray();
        }

        IEnumerable<TrayProviderPreference> candidates = allowed
            .Where(preference => preference.IsSelected);
        if (!candidates.Any())
        {
            candidates = allowed.Where(preference => preference.IsVisible);
        }

        return candidates
            .GroupBy(preference => preference.ProviderId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(providerCount)
            .ToArray();
    }

    private static TrayProviderSummary CreateProvider(
        TrayProviderPreference preference,
        IReadOnlyList<QuotaWindow> limits,
        DashboardProviderSummary? usage,
        TrayPopoverSettings popover,
        Func<string, string> getString)
    {
        limits ??= [];
        TrayMetricValue primary = Resolve(
            popover.PrimaryMetric,
            limits,
            usage,
            getString);
        TrayMetricValue secondary = popover.HasSecondaryMetric
            ? Resolve(popover.SecondaryMetric, limits, usage, getString)
            : TrayMetricValue.Absent;
        string automationName = popover.HasSecondaryMetric
            ? string.Format(
                CultureInfo.CurrentCulture,
                getString("TraySummaryProviderAutomationNameFormat"),
                preference.Name,
                primary.Label,
                primary.Value,
                secondary.Label,
                secondary.Value)
            : string.Format(
                CultureInfo.CurrentCulture,
                getString("TraySummaryProviderSingleAutomationNameFormat"),
                preference.Name,
                primary.Label,
                primary.Value);

        return new TrayProviderSummary(
            preference.ProviderId,
            preference.Name,
            popover.ShowProviderName,
            primary.Label,
            primary.ShortLabel,
            primary.Value,
            primary.Level,
            secondary.Label,
            secondary.ShortLabel,
            secondary.Value,
            secondary.Level,
            popover.HasSecondaryMetric,
            automationName);
    }

    private static TrayMetricValue Resolve(
        TrayPopoverMetric metric,
        IReadOnlyList<QuotaWindow> limits,
        DashboardProviderSummary? usage,
        Func<string, string> getString)
    {
        string unavailable = getString("TraySummaryUnavailableValue");
        switch (metric)
        {
            case TrayPopoverMetric.SessionQuota:
            {
                QuotaWindow? session = FindSession(
                    limits,
                    getString("TraySummarySessionLabel"));
                return new TrayMetricValue(
                    session?.Title ?? getString("TraySummarySessionLabel"),
                    getString("TraySummarySessionShortLabel"),
                    FormatRemaining(session, unavailable),
                    Evaluate(session));
            }

            case TrayPopoverMetric.PeriodQuota:
            {
                QuotaWindow? session = FindSession(
                    limits,
                    getString("TraySummarySessionLabel"));
                QuotaWindow? period = FindPeriod(limits, session, getString);
                return new TrayMetricValue(
                    period?.Title ?? getString("TraySummaryPeriodLabel"),
                    ResolvePeriodShortLabel(period, getString),
                    FormatRemaining(period, unavailable),
                    Evaluate(period));
            }

            case TrayPopoverMetric.SpendLast30Days:
                return new TrayMetricValue(
                    getString("TraySummarySpendLabel"),
                    getString("TraySummarySpendShortLabel"),
                    usage is { HasData: true, HasCostData: true }
                        ? string.Format(
                            CultureInfo.CurrentCulture,
                            getString("LocalUsageUsdCompactFormat"),
                            usage.CostUsd)
                        : unavailable,
                    Level: null);

            case TrayPopoverMetric.TokensLast30Days:
                return new TrayMetricValue(
                    getString("TraySummaryTokensLabel"),
                    getString("TraySummaryTokensShortLabel"),
                    usage is { HasData: true } ? usage.TokensText : unavailable,
                    Level: null);

            default:
                return TrayMetricValue.Absent;
        }
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
        metricId.StartsWith("quota.", StringComparison.Ordinal)
        && metricId.Count(character => character == '.') > 1;

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

    private sealed record TrayMetricValue(
        string Label,
        string ShortLabel,
        string Value,
        QuotaUsageLevel? Level)
    {
        public static TrayMetricValue Absent { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            Level: null);
    }
}
