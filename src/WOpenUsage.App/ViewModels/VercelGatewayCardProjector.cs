using System.Globalization;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels;

public static class VercelGatewayCardProjector
{
    private const string ProviderId = "vercel-ai-gateway";

    public static SampleProviderCard Create(
        ProviderSnapshot snapshot,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(snapshot.ProviderId.Value, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Vercel AI Gateway card requires its provider ID.",
                nameof(snapshot));
        }

        IReadOnlyDictionary<string, decimal> metrics = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .ToDictionary(metric => metric.Id.Value, metric => metric.Value, StringComparer.Ordinal);
        ProgressMetricSnapshot? quota = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .SingleOrDefault(metric => string.Equals(
                metric.Id.Value,
                "quota.gateway.key.budget",
                StringComparison.Ordinal));
        ProviderCapabilityState? quotaState = snapshot.Capabilities
            .FirstOrDefault(capability => string.Equals(
                capability.Id.Value,
                "quota.gateway.key.budget",
                StringComparison.Ordinal))
            ?.State;
        string missing = getString("CodexUsageMissing");
        string source = getString("VercelSourceValue");
        string observed = string.Format(
            CultureInfo.CurrentCulture,
            getString("ProviderObservedValueFormat"),
            snapshot.SourceObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

        return new SampleProviderCard(
            ProviderId,
            "Provider.VercelAiGateway",
            snapshot.DisplayName,
            getString("VercelExperimental"),
            getString("VercelCapabilityReport"),
            snapshot.Coverage == CoverageKind.Partial
                ? getString("VercelPartialReportNotice")
                : quotaState == ProviderCapabilityState.Degraded
                    ? getString("VercelQuotaDegradedNotice")
                    : getString("VercelReportLagNotice"),
            Windows: CreateQuotaWindows(quota, quotaState, getString),
            Metrics:
            [
                CurrencyMetric(
                    "VercelMetricTotalSpend",
                    "VercelGateway.TotalSpend30Days",
                    "spend.gateway.total.30d",
                    metrics,
                    missing,
                    getString),
                new SampleMetric(
                    getString("VercelMetricKeyBudget"),
                    QuotaStatus(quota, quotaState, getString),
                    "VercelGateway.KeyBudgetState"),
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
                snapshot.DisplayName));
    }

    private static IReadOnlyList<SampleQuotaWindow> CreateQuotaWindows(
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

        string title = getString("VercelQuotaTitle");
        string remaining = FormatRemaining(quota, getString);
        string reset = ResetText(quota.ResetCadence, getString);
        return
        [
            new SampleQuotaWindow(
                title,
                (double)quota.RemainingPercent,
                remaining,
                reset,
                string.Format(
                    CultureInfo.CurrentCulture,
                    getString("VercelQuotaAutomationFormat"),
                    title,
                    remaining,
                    reset),
                IsNearLimit: quota.RemainingPercent <= 20m),
        ];
    }

    private static string QuotaStatus(
        ProgressMetricSnapshot? quota,
        ProviderCapabilityState? quotaState,
        Func<string, string> getString) => quotaState switch
        {
            ProviderCapabilityState.Available when quota?.IsActive == false =>
                getString("VercelQuotaStatusInactive"),
            ProviderCapabilityState.Available when quota is not null =>
                FormatRemaining(quota, getString),
            ProviderCapabilityState.NotRequested =>
                getString("VercelQuotaStatusKeyIdMissing"),
            ProviderCapabilityState.NotConfigured =>
                getString("VercelQuotaStatusNoBudget"),
            ProviderCapabilityState.Degraded =>
                getString("VercelQuotaStatusDegraded"),
            _ => getString("ProviderStatusUnavailable"),
        };

    private static string FormatRemaining(
        ProgressMetricSnapshot quota,
        Func<string, string> getString) => string.Format(
        CultureInfo.CurrentCulture,
        getString("VercelQuotaRemainingFormat"),
        Math.Max(0m, quota.Limit - quota.Used),
        quota.Limit);

    private static string ResetText(
        ProgressResetCadence? cadence,
        Func<string, string> getString) => getString(cadence switch
        {
            ProgressResetCadence.Daily => "VercelQuotaResetDaily",
            ProgressResetCadence.Weekly => "VercelQuotaResetWeekly",
            ProgressResetCadence.Monthly => "VercelQuotaResetMonthly",
            ProgressResetCadence.Never => "VercelQuotaResetNever",
            _ => "ProviderStatusUnavailable",
        });

    private static SampleMetric CurrencyMetric(
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
            automationId);

    private static SampleMetric CountMetric(
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
            automationId);
}
