using System.Globalization;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Platform.Windows.Tests.VercelAiGateway;

public sealed class VercelGatewayCardProjectorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteSnapshotMapsAccountScopeSpendTokensAndProvenanceCopy()
    {
        SampleProviderCard card = VercelGatewayCardProjector.Create(
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
        Assert.Equal(6, card.SecondaryMetricItems.Count);
    }

    [Fact]
    public void PartialSnapshotShowsMissingValuesAndPartialNotice()
    {
        ProviderSnapshot snapshot = CreateSnapshot(CoverageKind.Partial, includeOutput: false);

        SampleProviderCard card = VercelGatewayCardProjector.Create(snapshot, Strings);

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

    private static SampleMetric Metric(SampleProviderCard card, string automationId) =>
        Assert.Single(card.Metrics, metric => metric.AutomationId == automationId);

    private static ProviderSnapshot CreateSnapshot(
        CoverageKind coverage,
        bool includeOutput = true,
        string providerId = "vercel-ai-gateway")
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

        return new ProviderSnapshot(
            new ProviderId(providerId),
            "Vercel AI Gateway",
            planLabel: null,
            ObservedAt,
            ObservedAt,
            "UTC",
            metrics,
            coverage,
            1);
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
        "VercelCapabilityReport" => "Account-wide spend and tokens · Last 30 days",
        "VercelPartialReportNotice" => "Some report fields are missing.",
        "VercelReportLagNotice" => "Reports can lag.",
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
        "ProviderSourceLabel" => "Source",
        "ProviderObservedLabel" => "Observed",
        "ProviderObservedValueFormat" => "Observed {0}",
        "ProviderDetailsTooltipFormat" => "{0}; {1}",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
        "LocalUsageUsdFormat" => "${0:N2}",
        _ => throw new KeyNotFoundException(key),
    };
}
