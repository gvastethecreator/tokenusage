using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Claude;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Projects the account-wide and scoped limits returned by Claude's account usage endpoint.
/// </summary>
public static class ClaudeDashboardProjector
{
    /// <summary>
    /// Two missed background refreshes make the reading visibly stale.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public static ProviderCard Create(
        ProviderSnapshot snapshot,
        TimeProvider clock,
        Func<string, string> getString,
        IReadOnlyDictionary<string, long>? windowUsedTokens = null,
        ProviderOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(snapshot.ProviderId.Value, "claude", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Claude quota card requires the Claude provider ID.",
                nameof(snapshot));
        }

        Dictionary<string, decimal> durations = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .Where(metric => metric.Id.Value.EndsWith(".window-minutes", StringComparison.Ordinal))
            .ToDictionary(
                metric => metric.Id.Value[..^".window-minutes".Length],
                metric => metric.Value,
                StringComparer.Ordinal);
        QuotaWindow[] windows = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .OrderBy(metric => Order(metric.Id.Value))
            .Select(metric => CreateWindow(
                metric,
                durations.GetValueOrDefault(metric.Id.Value),
                clock,
                getString,
                windowUsedTokens))
            .ToArray();
        string observed = snapshot.SourceObservedAtUtc.ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
        string source = getString("ClaudeQuotaSourceValue");
        string name = getString("LocalUsageAgentClaude");
        string? refreshError = outcome switch
        {
            ProviderOutcome.TransientFailure failure => failure.Error.Message,
            ProviderOutcome.ContractFailure failure => failure.Error.Message,
            _ => null,
        };
        return new ProviderCard(
            "claude",
            "Provider.Claude",
            name,
            snapshot.PlanLabel ?? getString("ClaudePlanSubscription"),
            getString("ClaudeCapabilityLimits"),
            NoticeText: refreshError is not null
                ? CodexDashboardProjector.Format(getString, "ClaudeQuotaRefreshFailedNotice", observed, refreshError)
                : outcome is ProviderOutcome.Throttled
                ? CodexDashboardProjector.Format(getString, "ClaudeQuotaThrottledNotice", observed)
                : SnapshotFreshness.IsStale(snapshot, clock, StaleAfter)
                ? CodexDashboardProjector.Format(getString, "ClaudeQuotaStaleNotice", observed)
                : null,
            Windows: windows,
            Metrics: [],
            SecondaryMetrics: [],
            SourceLabel: getString("ProviderSourceLabel"),
            SourceValue: source,
            ObservedLabel: getString("ProviderObservedLabel"),
            ObservedValue: CodexDashboardProjector.Format(
                getString,
                "ProviderObservedValueFormat",
                observed),
            DetailsTooltip: CodexDashboardProjector.Format(
                getString,
                "ProviderDetailsTooltipFormat",
                source,
                observed),
            DetailsAutomationName: CodexDashboardProjector.Format(
                getString,
                "ProviderDetailsAutomationNameFormat",
                name));
    }

    private static int Order(string metricId) => metricId switch
    {
        ClaudeRateLimitSnapshotMapper.FiveHourMetricId => 0,
        ClaudeRateLimitSnapshotMapper.SevenDayMetricId => 1,
        _ => 2,
    };

    private static QuotaWindow CreateWindow(
        ProgressMetricSnapshot metric,
        decimal durationMinutes,
        TimeProvider clock,
        Func<string, string> text,
        IReadOnlyDictionary<string, long>? windowUsedTokens)
    {
        double remaining = decimal.ToDouble(metric.RemainingPercent);
        double used = decimal.ToDouble(Math.Clamp(metric.Used / metric.Limit * 100m, 0m, 100m));
        string title = metric.Id.Value.StartsWith(ClaudeUsageSnapshotMapper.ScopedWeeklyMetricPrefix, StringComparison.Ordinal)
            ? CodexDashboardProjector.Format(text, "ClaudeWindowWeeklyScopeFormat", metric.LabelEvidence?.ProviderMetricName ?? string.Empty)
            : text(metric.Id.Value switch
        {
            ClaudeRateLimitSnapshotMapper.FiveHourMetricId => "ClaudeWindowFiveHour",
            ClaudeRateLimitSnapshotMapper.SevenDayMetricId => "ClaudeWindowWeeklyAll",
            _ => "ClaudeWindowSpendLimit",
        });
        string usage = CodexDashboardProjector.Format(text, "CodexUsageFormat", remaining, used);
        string reset = CodexDashboardProjector.FormatReset(metric.ResetsAtUtc, clock, text);
        (string? paceText, bool isPaceBehind) = CodexDashboardProjector.CreatePace(
            metric,
            durationMinutes,
            clock,
            text);
        string usedTokens = windowUsedTokens is not null
            && windowUsedTokens.TryGetValue(metric.Id.Value, out long usedTokenCount)
                ? CodexDashboardProjector.Format(
                    text,
                    "ClaudeQuotaUsedTokensFormat",
                    UsageValueFormatter.CompactTokens(usedTokenCount))
                : string.Empty;
        string provider = text("LocalUsageAgentClaude");
        return new QuotaWindow(
            title,
            remaining,
            usage,
            reset,
            usedTokens.Length == 0
                ? $"{provider}, {title}: {usage}. {reset}"
                : $"{provider}, {title}: {usage}. {reset}. {usedTokens}",
            remaining <= 15d,
            paceText,
            isPaceBehind,
            $"ClaudePace.{metric.Id.Value}",
            LayoutMetricId: metric.Id.Value,
            ResetAtUtc: metric.ResetsAtUtc,
            UsedText: usedTokens,
            LabelEvidenceText: text("CodexLabelProviderSupplied"))
        {
            UsedPercentText = CodexDashboardProjector.Format(text, "ClaudeUsedPercentFormat", used),
        };
    }
}
