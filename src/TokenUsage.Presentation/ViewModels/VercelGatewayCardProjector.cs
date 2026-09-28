using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Vercel AI Gateway card. Spend and tokens come from Custom Reporting scoped to the saved key
/// (<c>api_key_id=self</c>) over the last 30 UTC days. The credit balance is the team's.
/// </summary>
public static class VercelGatewayCardProjector
{
    private const string ProviderId = "vercel-ai-gateway";
    private const string BudgetMetricId = "quota.gateway.key.budget";
    private const string CreditsBalanceMetricId = "credits.gateway.balance";
    private const string CreditsTotalUsedMetricId = "credits.gateway.total-used";
    private const string ReportCapabilityId = "report.gateway.key";
    private const string TotalSpendMetricId = "spend.gateway.total.30d";

    public static ProviderCard Create(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        RequireVercel(snapshot, getString);

        IReadOnlyDictionary<string, decimal> metrics = ApiProviderCardParts.ScalarMetrics(snapshot);
        ProgressMetricSnapshot? quota = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .SingleOrDefault(metric => string.Equals(
                metric.Id.Value,
                BudgetMetricId,
                StringComparison.Ordinal));
        ProviderCapabilityState? quotaState = CapabilityState(snapshot, BudgetMetricId);
        ProviderCapabilityState? creditsState = CapabilityState(snapshot, CreditsBalanceMetricId);
        bool hasReport = CapabilityState(snapshot, ReportCapabilityId)
            != ProviderCapabilityState.Degraded;
        string missing = getString("CodexUsageMissing");
        string source = getString("VercelSourceValue");
        string observed = string.Format(
            CultureInfo.CurrentCulture,
            getString("ProviderObservedValueFormat"),
            snapshot.SourceObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

        return new ProviderCard(
            ProviderId,
            "Provider.VercelAiGateway",
            snapshot.DisplayName,
            getString("VercelExperimental"),
            getString("VercelCapabilityReport"),
            getString(!hasReport
                ? "VercelReportPlanNotice"
                : snapshot.Coverage == CoverageKind.Partial
                    ? "VercelPartialReportNotice"
                    : quotaState == ProviderCapabilityState.Degraded
                        ? "VercelQuotaDegradedNotice"
                        : creditsState == ProviderCapabilityState.Degraded
                            ? "VercelCreditsDegradedNotice"
                            : "VercelReportLagNotice"),
            Windows: CreateQuotaWindows(quota, quotaState, getString),
            Metrics:
            [
                CurrencyMetric(
                    "VercelMetricTotalSpend",
                    "VercelGateway.TotalSpend30Days",
                    TotalSpendMetricId,
                    metrics,
                    missing,
                    getString),
                CurrencyMetric(
                    "VercelMetricCreditBalance",
                    "VercelGateway.CreditBalance",
                    CreditsBalanceMetricId,
                    metrics,
                    missing,
                    getString),
                new DashboardMetric(
                    getString("VercelMetricKeyBudget"),
                    QuotaStatus(quota, quotaState, getString),
                    "VercelGateway.KeyBudgetState",
                    "quota.gateway.key.budget.status"),
                CountMetric(
                    "VercelMetricInputTokens",
                    "VercelGateway.InputTokens30Days",
                    "usage.tokens.input.30d",
                    metrics,
                    missing,
                    getString),
                CountMetric(
                    "VercelMetricOutputTokens",
                    "VercelGateway.OutputTokens30Days",
                    "usage.tokens.output.30d",
                    metrics,
                    missing,
                    getString),
                CountMetric(
                    "VercelMetricRequests",
                    "VercelGateway.Requests30Days",
                    "usage.requests.30d",
                    metrics,
                    missing,
                    getString),
            ],
            SecondaryMetrics:
            [
                CurrencyMetric("VercelMetricMarketValue", "VercelGateway.MarketValue30Days", "spend.gateway.market.30d", metrics, missing, getString),
                CurrencyMetric("VercelMetricSurcharge", "VercelGateway.Surcharge30Days", "spend.gateway.surcharge.30d", metrics, missing, getString),
                CurrencyMetric("VercelMetricGatewayFee", "VercelGateway.Fee30Days", "spend.gateway.fee.30d", metrics, missing, getString),
                CountMetric("VercelMetricCachedInputTokens", "VercelGateway.CachedInputTokens30Days", "usage.tokens.cached-input.30d", metrics, missing, getString),
                CountMetric("VercelMetricCacheCreationTokens", "VercelGateway.CacheCreationTokens30Days", "usage.tokens.cache-creation-input.30d", metrics, missing, getString),
                CountMetric("VercelMetricReasoningTokens", "VercelGateway.ReasoningTokens30Days", "usage.tokens.reasoning.30d", metrics, missing, getString),
            ],
            SourceLabel: getString("ProviderSourceLabel"),
            SourceValue: source,
            ObservedLabel: getString("ProviderObservedLabel"),
            ObservedValue: observed,
            DetailsTooltip: string.Format(
                CultureInfo.CurrentCulture,
                getString("ProviderDetailsTooltipFormat"),
                source,
                snapshot.SourceObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            DetailsAutomationName: string.Format(
                CultureInfo.CurrentCulture,
                getString("ProviderDetailsAutomationNameFormat"),
                snapshot.DisplayName),
            CreditSummary: CreateCreditSummary(metrics, getString));
    }

    public static SpendSlice? CreateSpendSlice(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        RequireVercel(snapshot, getString);
        if (!ApiProviderCardParts.ScalarMetrics(snapshot)
                .TryGetValue(TotalSpendMetricId, out decimal total)
            || total <= 0m)
        {
            return null;
        }

        return new SpendSlice(
            ProviderId,
            snapshot.DisplayName,
            decimal.ToDouble(total),
            string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsageUsdFormat"),
                total),
            CompactAmountText: string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsageUsdCompactFormat"),
                total));
    }

    /// <summary>
    /// The panel row: the key's spend and input plus output tokens over the last 30 days,
    /// the same period as the local rows. Without a report both stay unknown.
    /// </summary>
    public static DashboardProviderSummary CreateSummary(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        RequireVercel(snapshot, getString);
        IReadOnlyDictionary<string, decimal> metrics = ApiProviderCardParts.ScalarMetrics(snapshot);
        decimal? spend = metrics.TryGetValue(TotalSpendMetricId, out decimal total)
            ? total
            : null;
        long? tokens = metrics.TryGetValue("usage.tokens.input.30d", out decimal input)
            && metrics.TryGetValue("usage.tokens.output.30d", out decimal output)
                ? decimal.ToInt64(input + output)
                : null;
        return ApiProviderCardParts.CreateSummary(ProviderId, spend, tokens, getString);
    }

    private static void RequireVercel(ProviderSnapshot snapshot, Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(snapshot.ProviderId.Value, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Vercel AI Gateway card requires its provider ID.",
                nameof(snapshot));
        }
    }

    private static ProviderCapabilityState? CapabilityState(
        ProviderSnapshot snapshot,
        string capabilityId) =>
        snapshot.Capabilities
            .FirstOrDefault(capability => string.Equals(
                capability.Id.Value,
                capabilityId,
                StringComparison.Ordinal))
            ?.State;

    private static ProviderCreditSummary? CreateCreditSummary(
        IReadOnlyDictionary<string, decimal> metrics,
        Func<string, string> getString)
    {
        if (!metrics.TryGetValue(CreditsBalanceMetricId, out decimal balance))
        {
            return null;
        }

        string detail = metrics.TryGetValue(CreditsTotalUsedMetricId, out decimal totalUsed)
            ? ApiProviderCardParts.Format(getString, "VercelCreditSummaryDetailFormat", totalUsed)
            : getString("VercelCreditSummaryDetail");
        return new ProviderCreditSummary(
            getString("VercelCreditSummaryTitle"),
            UsageValueFormatter.Usd(balance, getString),
            detail,
            HasAvailableCredits: balance > 0m,
            IsExpired: false);
    }

    private static IReadOnlyList<QuotaWindow> CreateQuotaWindows(
        ProgressMetricSnapshot? quota,
        ProviderCapabilityState? quotaState,
        Func<string, string> getString)
    {
        if (quota is null
            || quotaState != ProviderCapabilityState.Available
            || quota.IsActive != true)
        {
            return [];
        }

        return [ApiProviderCardParts.CreateSpendLimitWindow(
            getString("VercelQuotaTitle"),
            quota,
            getString)];
    }

    private static string QuotaStatus(
        ProgressMetricSnapshot? quota,
        ProviderCapabilityState? quotaState,
        Func<string, string> getString) => quotaState switch
        {
            ProviderCapabilityState.Available when quota?.IsActive == false =>
                getString("VercelQuotaStatusInactive"),
            ProviderCapabilityState.Available when quota is not null =>
                ApiProviderCardParts.Format(
                    getString,
                    "ApiLimitRemainingFormat",
                    Math.Max(0m, quota.Limit - quota.Used),
                    quota.Limit),
            ProviderCapabilityState.NotRequested =>
                getString("VercelQuotaStatusKeyIdMissing"),
            ProviderCapabilityState.NotConfigured =>
                getString("VercelQuotaStatusNoBudget"),
            ProviderCapabilityState.Degraded =>
                getString("VercelQuotaStatusDegraded"),
            _ => getString("ProviderStatusUnavailable"),
        };

    private static DashboardMetric CurrencyMetric(
        string labelKey,
        string automationId,
        string metricId,
        IReadOnlyDictionary<string, decimal> metrics,
        string missing,
        Func<string, string> getString) => new(
            getString(labelKey),
            metrics.TryGetValue(metricId, out decimal value)
                ? string.Format(CultureInfo.CurrentCulture, getString("LocalUsageUsdFormat"), value)
                : missing,
            automationId,
            metricId);

    private static DashboardMetric CountMetric(
        string labelKey,
        string automationId,
        string metricId,
        IReadOnlyDictionary<string, decimal> metrics,
        string missing,
        Func<string, string> getString) => new(
            getString(labelKey),
            metrics.TryGetValue(metricId, out decimal value)
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : missing,
            automationId,
            metricId);
}
