using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// OpenRouter card, read from <c>GET /api/v1/key</c> with the saved key. Spend is per UTC
/// calendar period (today, this week from Monday, this month), not a rolling window, and the
/// key endpoint reports no token counts.
/// </summary>
public static class OpenRouterCardProjector
{
    private const string ProviderId = "openrouter";
    private const string KeyLimitMetricId = "quota.openrouter.key.limit";
    private const string SpendMonthMetricId = "spend.openrouter.month";

    public static ProviderCard Create(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        RequireOpenRouter(snapshot, getString);
        IReadOnlyDictionary<string, decimal> metrics = ApiProviderCardParts.ScalarMetrics(snapshot);
        ProgressMetricSnapshot? limit = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .SingleOrDefault(metric => string.Equals(
                metric.Id.Value,
                KeyLimitMetricId,
                StringComparison.Ordinal));
        string missing = getString("CodexUsageMissing");
        string source = getString("OpenRouterSourceValue");
        string observedAt = snapshot.SourceObservedAtUtc
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);

        return new ProviderCard(
            ProviderId,
            "Provider.OpenRouter",
            snapshot.DisplayName,
            getString(string.Equals(snapshot.PlanLabel, "free", StringComparison.Ordinal)
                ? "OpenRouterPlanFree"
                : "OpenRouterPlanPaid"),
            getString("OpenRouterCapability"),
            getString("OpenRouterPeriodNotice"),
            Windows: limit is null
                ? []
                : [ApiProviderCardParts.CreateSpendLimitWindow(
                    getString("OpenRouterLimitTitle"),
                    limit,
                    getString)],
            Metrics:
            [
                Spend("OpenRouterMetricToday", "OpenRouter.SpendToday", "spend.openrouter.day", metrics, missing, getString),
                Spend("OpenRouterMetricWeek", "OpenRouter.SpendWeek", "spend.openrouter.week", metrics, missing, getString),
                Spend("OpenRouterMetricMonth", "OpenRouter.SpendMonth", SpendMonthMetricId, metrics, missing, getString),
                new DashboardMetric(
                    getString("OpenRouterMetricKeyLimit"),
                    limit is null
                        ? getString("OpenRouterLimitNone")
                        : ApiProviderCardParts.Format(
                            getString,
                            "ApiLimitRemainingFormat",
                            Math.Max(0m, limit.Limit - limit.Used),
                            limit.Limit),
                    "OpenRouter.KeyLimitState",
                    "quota.openrouter.key.limit.status"),
            ],
            SecondaryMetrics:
            [
                Spend("OpenRouterMetricTotal", "OpenRouter.SpendTotal", "spend.openrouter.total", metrics, missing, getString),
            ],
            SourceLabel: getString("ProviderSourceLabel"),
            SourceValue: source,
            ObservedLabel: getString("ProviderObservedLabel"),
            ObservedValue: string.Format(
                CultureInfo.CurrentCulture,
                getString("ProviderObservedValueFormat"),
                observedAt),
            DetailsTooltip: string.Format(
                CultureInfo.CurrentCulture,
                getString("ProviderDetailsTooltipFormat"),
                source,
                observedAt),
            DetailsAutomationName: string.Format(
                CultureInfo.CurrentCulture,
                getString("ProviderDetailsAutomationNameFormat"),
                snapshot.DisplayName));
    }

    /// <summary>
    /// The panel row: this UTC month's spend for the key. OpenRouter's key endpoint has no
    /// token counts, so tokens stay unknown.
    /// </summary>
    public static DashboardProviderSummary CreateSummary(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        RequireOpenRouter(snapshot, getString);
        decimal? month = ApiProviderCardParts.ScalarMetrics(snapshot)
            .TryGetValue(SpendMonthMetricId, out decimal value)
                ? value
                : null;
        return ApiProviderCardParts.CreateSummary(ProviderId, month, tokens: null, getString);
    }

    private static void RequireOpenRouter(ProviderSnapshot snapshot, Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(snapshot.ProviderId.Value, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The OpenRouter card requires its provider ID.",
                nameof(snapshot));
        }
    }

    private static DashboardMetric Spend(
        string labelKey,
        string automationId,
        string metricId,
        IReadOnlyDictionary<string, decimal> metrics,
        string missing,
        Func<string, string> getString) => new(
            getString(labelKey),
            metrics.TryGetValue(metricId, out decimal value)
                ? UsageValueFormatter.Usd(value, getString)
                : missing,
            automationId,
            metricId);
}
