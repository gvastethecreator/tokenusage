using System.Globalization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Providers;

namespace TokenUsage.Platform.Windows.Tests.VercelAiGateway;

public sealed class VercelGatewayCardProjectorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteSnapshotMapsAccountScopeSpendTokensAndProvenanceCopy()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(CoverageKind.Complete),
            Strings);

        Assert.Equal("vercel-ai-gateway", card.ProviderId);
        Assert.Equal("Provider.VercelAiGateway", card.AutomationId);
        Assert.Equal("Experimental", card.PlanLabel);
        Assert.Contains("Account-wide", card.CapabilityLabel, StringComparison.Ordinal);
        Assert.Contains("lag", card.NoticeText, StringComparison.Ordinal);
        Assert.Contains("Manual key", card.SourceValue, StringComparison.Ordinal);
        Assert.Contains("Account-wide", card.SourceValue, StringComparison.Ordinal);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2}", 12.5m),
            Metric(card, "VercelGateway.TotalSpend30Days").Value);
        Assert.Equal(
            1000m.ToString("N0", CultureInfo.CurrentCulture),
            Metric(card, "VercelGateway.InputTokens30Days").Value);
        Assert.Equal(
            250m.ToString("N0", CultureInfo.CurrentCulture),
            Metric(card, "VercelGateway.OutputTokens30Days").Value);
        Assert.Equal(
            7m.ToString("N0", CultureInfo.CurrentCulture),
            Metric(card, "VercelGateway.Requests30Days").Value);
        Assert.Equal(
            "Add a key ID to check",
            Metric(card, "VercelGateway.KeyBudgetState").Value);
        Assert.Empty(card.Windows);
        Assert.Equal(6, card.SecondaryMetricItems.Count);
    }

    [Fact]
    public void AvailableBudgetMapsAnimatedWindowAndUsdRemaining()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: ProviderCapabilityState.Available,
                includeBudget: true),
            Strings);

        QuotaWindow window = Assert.Single(card.Windows);
        Assert.Equal(65d, window.RemainingPercent);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2} left of ${1:N2}", 6.5m, 10m),
            window.RemainingText);
        Assert.Equal("Resets monthly (UTC)", window.ResetText);
        Assert.False(window.IsNearLimit);
        Assert.Equal(
            window.RemainingText,
            Metric(card, "VercelGateway.KeyBudgetState").Value);
    }

    [Fact]
    public void DegradedBudgetKeepsReportNoticeScopedToBudget()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: ProviderCapabilityState.Degraded),
            Strings);

        Assert.Empty(card.Windows);
        Assert.Contains("budget", card.NoticeText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Budget unavailable",
            Metric(card, "VercelGateway.KeyBudgetState").Value);
    }

    [Fact]
    public void MissingBudgetKeepsReportWithoutProgressWindow()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: ProviderCapabilityState.NotConfigured),
            Strings);

        Assert.Empty(card.Windows);
        Assert.Equal(
            "No budget set",
            Metric(card, "VercelGateway.KeyBudgetState").Value);
    }

    [Fact]
    public void InactiveBudgetDoesNotRenderAProgressWindow()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: ProviderCapabilityState.Available,
                includeBudget: true,
                budgetIsActive: false),
            Strings);

        Assert.Empty(card.Windows);
        Assert.Equal(
            "Budget inactive",
            Metric(card, "VercelGateway.KeyBudgetState").Value);
    }

    [Fact]
    public void PartialSnapshotShowsMissingValuesAndPartialNotice()
    {
        ProviderSnapshot snapshot = CreateSnapshot(CoverageKind.Partial, includeOutput: false);

        ProviderCard card = VercelGatewayCardProjector.Create(snapshot, Strings);

        Assert.Contains("missing", card.NoticeText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Missing", Metric(card, "VercelGateway.OutputTokens30Days").Value);
    }

    [Fact]
    public void WrongProviderIsRejected()
    {
        ProviderSnapshot snapshot = CreateSnapshot(CoverageKind.Complete, providerId: "codex");

        Assert.Throws<ArgumentException>(() =>
            VercelGatewayCardProjector.Create(snapshot, Strings));
    }

    private static DashboardMetric Metric(ProviderCard card, string automationId) =>
        Assert.Single(card.Metrics, metric => metric.AutomationId == automationId);

    private static ProviderSnapshot CreateSnapshot(
        CoverageKind coverage,
        bool includeOutput = true,
        string providerId = "vercel-ai-gateway",
        ProviderCapabilityState quotaState = ProviderCapabilityState.NotRequested,
        bool includeBudget = false,
        bool budgetIsActive = true)
    {
        var metrics = new List<MetricSnapshot>
        {
            Scalar("spend.gateway.total.30d", 12.5m, "usd"),
            Scalar("spend.gateway.market.30d", 11m, "usd"),
            Scalar("spend.gateway.surcharge.30d", 1m, "usd"),
            Scalar("spend.gateway.fee.30d", 0.5m, "usd"),
            Scalar("usage.tokens.input.30d", 1000m, "tokens"),
            Scalar("usage.tokens.cached-input.30d", 100m, "tokens"),
            Scalar("usage.tokens.cache-creation-input.30d", 50m, "tokens"),
            Scalar("usage.tokens.reasoning.30d", 25m, "tokens"),
            Scalar("usage.requests.30d", 7m, "requests"),
        };
        if (includeOutput)
        {
            metrics.Add(Scalar("usage.tokens.output.30d", 250m, "tokens"));
        }

        if (includeBudget)
        {
            metrics.Add(new ProgressMetricSnapshot(
                new MetricId("quota.gateway.key.budget"),
                3.5m,
                10m,
                resetsAtUtc: null,
                new DataProvenance(
                    SourceKind.ManualKey,
                    MeasurementKind.ProviderReported,
                    "vercel-ai-gateway-quota/1"),
                "usd",
                ProgressResetCadence.Monthly,
                isActive: budgetIsActive));
        }

        return new ProviderSnapshot(
            new ProviderId(providerId),
            "Vercel AI Gateway",
            planLabel: null,
            ObservedAt,
            ObservedAt,
            "UTC",
            metrics,
            coverage,
            2,
            [
                new ProviderCapabilitySnapshot(
                    new CapabilityId("quota.gateway.key.budget"),
                    quotaState,
                    new DataProvenance(
                        SourceKind.ManualKey,
                        MeasurementKind.Derived,
                        "vercel-ai-gateway-quota-state/1")),
            ]);
    }

    private static ScalarMetricSnapshot Scalar(string id, decimal value, string unit) => new(
        new MetricId(id),
        value,
        unit,
        new DataProvenance(
            SourceKind.ManualKey,
            MeasurementKind.ProviderReported,
            "vercel-ai-gateway-report/1"));

    private static string Strings(string key) => key switch
    {
        "CodexUsageMissing" => "Missing",
        "VercelExperimental" => "Experimental",
        "VercelCapabilityReport" => "Account-wide report · Optional per-key budget",
        "VercelPartialReportNotice" => "Some report fields are missing.",
        "VercelReportLagNotice" => "Reports can lag.",
        "VercelQuotaDegradedNotice" => "The report is current. The key budget could not be checked.",
        "VercelSourceValue" => "Official report · Manual key · Account-wide",
        "VercelMetricTotalSpend" => "Gateway spend",
        "VercelMetricMarketValue" => "Market value",
        "VercelMetricSurcharge" => "Surcharge",
        "VercelMetricGatewayFee" => "Gateway fee",
        "VercelMetricInputTokens" => "Input tokens",
        "VercelMetricOutputTokens" => "Output tokens",
        "VercelMetricCachedInputTokens" => "Cached input tokens",
        "VercelMetricCacheCreationTokens" => "Cache creation tokens",
        "VercelMetricReasoningTokens" => "Reasoning tokens",
        "VercelMetricRequests" => "Requests",
        "VercelMetricKeyBudget" => "Key budget",
        "VercelQuotaTitle" => "API key budget",
        "VercelQuotaRemainingFormat" => "${0:N2} left of ${1:N2}",
        "VercelQuotaAutomationFormat" => "{0}: {1}. {2}",
        "VercelQuotaResetDaily" => "Resets daily (UTC)",
        "VercelQuotaResetWeekly" => "Resets weekly (UTC)",
        "VercelQuotaResetMonthly" => "Resets monthly (UTC)",
        "VercelQuotaResetNever" => "No reset",
        "VercelQuotaStatusKeyIdMissing" => "Add a key ID to check",
        "VercelQuotaStatusNoBudget" => "No budget set",
        "VercelQuotaStatusDegraded" => "Budget unavailable",
        "VercelQuotaStatusInactive" => "Budget inactive",
        "ProviderStatusUnavailable" => "Unavailable",
        "ProviderSourceLabel" => "Source",
        "ProviderObservedLabel" => "Observed",
        "ProviderObservedValueFormat" => "Observed {0}",
        "ProviderDetailsTooltipFormat" => "{0}; {1}",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
        "LocalUsageUsdFormat" => "${0:N2}",
        _ => throw new KeyNotFoundException(key),
    };
}
