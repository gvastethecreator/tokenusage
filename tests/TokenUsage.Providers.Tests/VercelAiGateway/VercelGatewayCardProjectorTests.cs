using System.Globalization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayCardProjectorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteSnapshotMapsKeySpendTokensBalanceAndProvenanceCopy()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(CoverageKind.Complete),
            Strings);

        Assert.Equal("vercel-ai-gateway", card.ProviderId);
        Assert.Equal("Provider.VercelAiGateway", card.AutomationId);
        Assert.Equal("Experimental", card.PlanLabel);
        Assert.Contains("this key", card.CapabilityLabel, StringComparison.Ordinal);
        Assert.Contains("lag", card.NoticeText, StringComparison.Ordinal);
        Assert.Contains("this key only", card.SourceValue, StringComparison.Ordinal);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2}", 12.5m),
            Metric(card, "VercelGateway.TotalSpend30Days").Value);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2}", 95.5m),
            Metric(card, "VercelGateway.CreditBalance").Value);
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
    public void CreditBalanceBecomesTheProviderCreditSummary()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(CoverageKind.Complete),
            Strings);

        ProviderCreditSummary summary = Assert.IsType<ProviderCreditSummary>(card.CreditSummary);
        Assert.Equal("AI Gateway credits", summary.Title);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, "${0:N2}", 95.5m), summary.Value);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "Team balance. ${0:N2} used", 4.5m),
            summary.Detail);
        Assert.True(summary.HasAvailableCredits);
    }

    [Fact]
    public void AvailableBudgetMapsWindowWithUsdAmountsAndNextUtcReset()
    {
        var resetsAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: ProviderCapabilityState.Available,
                includeBudget: true,
                budgetResetsAt: resetsAt),
            Strings);

        QuotaWindow window = Assert.Single(card.Windows);
        Assert.Equal(65d, window.RemainingPercent);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2} left of ${1:N2}", 6.5m, 10m),
            window.RemainingText);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2} of ${1:N2} used", 3.5m, 10m),
            window.UsedText);
        Assert.Equal("Resets monthly (UTC)", window.ResetText);
        Assert.Equal(resetsAt, window.ResetAtUtc);
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

    [Theory]
    [InlineData(ProviderCapabilityState.NotConfigured, false, true, "No budget set")]
    [InlineData(ProviderCapabilityState.Available, true, false, "Budget inactive")]
    public void BudgetWithoutAnActiveLimitRendersNoWindow(
        ProviderCapabilityState quotaState,
        bool includeBudget,
        bool budgetIsActive,
        string expectedState)
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(
                CoverageKind.Complete,
                quotaState: quotaState,
                includeBudget: includeBudget,
                budgetIsActive: budgetIsActive),
            Strings);

        Assert.Empty(card.Windows);
        Assert.Equal(expectedState, Metric(card, "VercelGateway.KeyBudgetState").Value);
    }

    [Fact]
    public void PartialSnapshotShowsMissingValuesAndPartialNotice()
    {
        ProviderCard card = VercelGatewayCardProjector.Create(
            CreateSnapshot(CoverageKind.Partial, includeOutput: false),
            Strings);

        Assert.Contains("missing", card.NoticeText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Missing", Metric(card, "VercelGateway.OutputTokens30Days").Value);
    }

    [Fact]
    public void PlanWithoutReportShowsOnlyTheBalanceAndSaysWhy()
    {
        ProviderSnapshot snapshot = CreateSnapshot(
            CoverageKind.Partial,
            includeReport: false);

        ProviderCard card = VercelGatewayCardProjector.Create(snapshot, Strings);
        DashboardProviderSummary summary = VercelGatewayCardProjector.CreateSummary(snapshot, Strings);

        Assert.Contains("Pro or Enterprise", card.NoticeText, StringComparison.Ordinal);
        Assert.Equal("Missing", Metric(card, "VercelGateway.TotalSpend30Days").Value);
        Assert.NotNull(card.CreditSummary);
        Assert.False(summary.HasData);
        Assert.Equal("—", summary.CostText);
    }

    [Fact]
    public void SummaryUsesTheKeySpendAndTokensForTheLast30Days()
    {
        DashboardProviderSummary summary = VercelGatewayCardProjector.CreateSummary(
            CreateSnapshot(CoverageKind.Complete),
            Strings);

        Assert.Equal("vercel-ai-gateway", summary.ProviderId);
        Assert.Equal(12.5m, summary.CostUsd);
        Assert.Equal(1250, summary.TotalTokens);
        Assert.True(summary.HasData);
        Assert.Equal(0d, summary.SharePercent);
    }

    [Fact]
    public void PositiveKeySpendCreatesALiveDashboardSlice()
    {
        SpendSlice slice = Assert.IsType<SpendSlice>(
            VercelGatewayCardProjector.CreateSpendSlice(
                CreateSnapshot(CoverageKind.Complete),
                Strings));

        Assert.Equal("vercel-ai-gateway", slice.ProviderId);
        Assert.Equal("Vercel AI Gateway", slice.ProviderName);
        Assert.Equal(12.5d, slice.Amount);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, "${0:N2}", 12.5m), slice.AmountText);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N0}", 12.5m),
            slice.CompactAmountText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrZeroKeySpendDoesNotCreateAnEmptySlice(bool zeroSpend)
    {
        ProviderSnapshot snapshot = CreateSnapshot(
            CoverageKind.Complete,
            includeReport: zeroSpend,
            totalSpend: 0m);

        Assert.Null(VercelGatewayCardProjector.CreateSpendSlice(snapshot, Strings));
    }

    [Fact]
    public void WrongProviderIsRejected()
    {
        ProviderSnapshot snapshot = CreateSnapshot(CoverageKind.Complete, providerId: "codex");

        Assert.Throws<ArgumentException>(() =>
            VercelGatewayCardProjector.Create(snapshot, Strings));
        Assert.Throws<ArgumentException>(() =>
            VercelGatewayCardProjector.CreateSpendSlice(snapshot, Strings));
    }

    private static DashboardMetric Metric(ProviderCard card, string automationId) =>
        Assert.Single(card.Metrics, metric => metric.AutomationId == automationId);

    private static ProviderSnapshot CreateSnapshot(
        CoverageKind coverage,
        bool includeOutput = true,
        string providerId = "vercel-ai-gateway",
        ProviderCapabilityState quotaState = ProviderCapabilityState.NotRequested,
        bool includeBudget = false,
        bool budgetIsActive = true,
        DateTimeOffset? budgetResetsAt = null,
        bool includeReport = true,
        decimal totalSpend = 12.5m)
    {
        var metrics = new List<MetricSnapshot>
        {
            Scalar("credits.gateway.balance", 95.5m, "usd"),
            Scalar("credits.gateway.total-used", 4.5m, "usd"),
        };
        if (includeReport)
        {
            metrics.AddRange(
            [
                Scalar("spend.gateway.total.30d", totalSpend, "usd"),
                Scalar("spend.gateway.market.30d", 11m, "usd"),
                Scalar("spend.gateway.surcharge.30d", 1m, "usd"),
                Scalar("spend.gateway.fee.30d", 0.5m, "usd"),
                Scalar("usage.tokens.input.30d", 1000m, "tokens"),
                Scalar("usage.tokens.cached-input.30d", 100m, "tokens"),
                Scalar("usage.tokens.cache-creation-input.30d", 50m, "tokens"),
                Scalar("usage.tokens.reasoning.30d", 25m, "tokens"),
                Scalar("usage.requests.30d", 7m, "requests"),
            ]);
            if (includeOutput)
            {
                metrics.Add(Scalar("usage.tokens.output.30d", 250m, "tokens"));
            }
        }

        if (includeBudget)
        {
            metrics.Add(new ProgressMetricSnapshot(
                new MetricId("quota.gateway.key.budget"),
                3.5m,
                10m,
                budgetResetsAt,
                new DataProvenance(
                    SourceKind.ManualKey,
                    MeasurementKind.ProviderReported,
                    "vercel-ai-gateway-quota/1"),
                "usd",
                ProgressResetCadence.Monthly,
                isActive: budgetIsActive));
        }

        DataProvenance provenance = new(
            SourceKind.ManualKey,
            MeasurementKind.Derived,
            "test");
        return new ProviderSnapshot(
            new ProviderId(providerId),
            "Vercel AI Gateway",
            planLabel: null,
            ObservedAt,
            ObservedAt,
            "UTC",
            metrics,
            coverage,
            3,
            [
                new ProviderCapabilitySnapshot(
                    new CapabilityId("quota.gateway.key.budget"),
                    quotaState,
                    provenance),
                new ProviderCapabilitySnapshot(
                    new CapabilityId("credits.gateway.balance"),
                    ProviderCapabilityState.Available,
                    provenance),
                new ProviderCapabilitySnapshot(
                    new CapabilityId("report.gateway.key"),
                    includeReport
                        ? ProviderCapabilityState.Available
                        : ProviderCapabilityState.Degraded,
                    provenance),
            ]);
    }

    private static ScalarMetricSnapshot Scalar(string id, decimal value, string unit) => new(
        new MetricId(id),
        value,
        unit,
        new DataProvenance(
            SourceKind.ManualKey,
            MeasurementKind.ProviderReported,
            "vercel-ai-gateway-report/2"));

    private static string Strings(string key) => key switch
    {
        "CodexUsageMissing" => "Missing",
        "VercelExperimental" => "Experimental",
        "VercelCapabilityReport" => "Spend for this key · Team credit balance · Optional key budget",
        "VercelPartialReportNotice" => "Some report fields are missing.",
        "VercelReportLagNotice" => "Reports can lag.",
        "VercelReportPlanNotice" => "Custom Reporting needs a Vercel Pro or Enterprise plan.",
        "VercelQuotaDegradedNotice" => "The report is current. The key budget could not be checked.",
        "VercelCreditsDegradedNotice" => "The team credit balance could not be checked.",
        "VercelSourceValue" => "Official API · Saved key · Spend for this key only",
        "VercelMetricTotalSpend" => "Gateway spend",
        "VercelMetricCreditBalance" => "Team credit balance",
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
        "VercelQuotaStatusKeyIdMissing" => "Add a key ID to check",
        "VercelQuotaStatusNoBudget" => "No budget set",
        "VercelQuotaStatusDegraded" => "Budget unavailable",
        "VercelQuotaStatusInactive" => "Budget inactive",
        "VercelCreditSummaryTitle" => "AI Gateway credits",
        "VercelCreditSummaryDetail" => "Team balance",
        "VercelCreditSummaryDetailFormat" => "Team balance. ${0:N2} used",
        "ApiLimitRemainingFormat" => "${0:N2} left of ${1:N2}",
        "ApiLimitUsedFormat" => "${0:N2} of ${1:N2} used",
        "ApiLimitAutomationFormat" => "{0}: {1}. {2}",
        "ApiLimitResetDaily" => "Resets daily (UTC)",
        "ApiLimitResetWeekly" => "Resets weekly on Monday (UTC)",
        "ApiLimitResetMonthly" => "Resets monthly (UTC)",
        "ApiLimitResetNever" => "No reset",
        "ApiLimitResetUnknown" => "Reset schedule not reported",
        "ProviderStatusUnavailable" => "Unavailable",
        "ProviderSourceLabel" => "Source",
        "ProviderObservedLabel" => "Observed",
        "ProviderObservedValueFormat" => "Observed {0}",
        "ProviderDetailsTooltipFormat" => "{0}; {1}",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
        "LocalUsageUsdFormat" => "${0:N2}",
        "LocalUsageUsdTinyFormat" => "{0}",
        "LocalUsageUsdCompactFormat" => "${0:N0}",
        _ => throw new KeyNotFoundException(key),
    };
}
