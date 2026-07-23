using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.VercelAiGateway;

internal static class VercelGatewaySnapshotMapper
{
    internal const string AdapterVersion = "vercel-ai-gateway-report/1";
    internal const int AdapterContractVersion = 1;
    internal const string TimeZoneId = "UTC";

    private static readonly DataProvenance Provenance = new DataProvenance(
        SourceKind.ManualKey,
        MeasurementKind.ProviderReported,
        AdapterVersion);

    internal sealed class MapResult
    {
        public MapResult(ProviderSnapshot snapshot, IReadOnlyList<ProviderWarning> warnings)
        {
            Snapshot = snapshot;
            Warnings = warnings;
        }

        public ProviderSnapshot Snapshot { get; }
        public IReadOnlyList<ProviderWarning> Warnings { get; }
    }

    internal static MapResult Map(VercelGatewayReport report, DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        var rows = report.Results;
        if (rows.Count == 0)
        {
            return new MapResult(
                CreateSnapshot(fetchedAtUtc, Array.Empty<MetricSnapshot>(), CoverageKind.Complete),
                Array.Empty<ProviderWarning>());
        }

        var metrics = new List<MetricSnapshot>();
        var warnings = new List<ProviderWarning>();

        TryAddDecimalMetric(
            metrics,
            warnings,
            rows,
            "spend.gateway.total.30d",
            "usd",
            static r => r.TotalCost);

        TryAddDecimalMetric(
            metrics,
            warnings,
            rows,
            "spend.gateway.market.30d",
            "usd",
            static r => r.MarketCost);

        TryAddDecimalMetric(
            metrics,
            warnings,
            rows,
            "spend.gateway.surcharge.30d",
            "usd",
            static r => r.SurchargeCost);

        TryAddDecimalMetric(
            metrics,
            warnings,
            rows,
            "spend.gateway.fee.30d",
            "usd",
            static r => r.GatewayCost);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.tokens.input.30d",
            "tokens",
            static r => r.InputTokens);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.tokens.output.30d",
            "tokens",
            static r => r.OutputTokens);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.tokens.cached-input.30d",
            "tokens",
            static r => r.CachedInputTokens);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.tokens.cache-creation-input.30d",
            "tokens",
            static r => r.CacheCreationInputTokens);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.tokens.reasoning.30d",
            "tokens",
            static r => r.ReasoningTokens);

        TryAddLongMetric(
            metrics,
            warnings,
            rows,
            "usage.requests.30d",
            "requests",
            static r => r.RequestCount);

        var coverage = warnings.Count > 0 ? CoverageKind.Partial : CoverageKind.Complete;
        return new MapResult(CreateSnapshot(fetchedAtUtc, metrics, coverage), warnings);
    }

    private static void TryAddDecimalMetric(
        List<MetricSnapshot> metrics,
        List<ProviderWarning> warnings,
        IReadOnlyList<VercelGatewayDailyReportRow> rows,
        string metricId,
        string unit,
        Func<VercelGatewayDailyReportRow, decimal?> selector)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (!selector(rows[i]).HasValue)
            {
                warnings.Add(new ProviderWarning(
                    ProviderWarningCode.MissingMetric,
                    $"Metric '{metricId}' is missing from the Vercel AI Gateway report."));
                return;
            }
        }

        decimal sum = 0m;
        for (var i = 0; i < rows.Count; i++)
        {
            sum = checked(sum + selector(rows[i])!.Value);
        }

        metrics.Add(new ScalarMetricSnapshot(
            new MetricId(metricId),
            sum,
            unit,
            Provenance));
    }

    private static void TryAddLongMetric(
        List<MetricSnapshot> metrics,
        List<ProviderWarning> warnings,
        IReadOnlyList<VercelGatewayDailyReportRow> rows,
        string metricId,
        string unit,
        Func<VercelGatewayDailyReportRow, long?> selector)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (!selector(rows[i]).HasValue)
            {
                warnings.Add(new ProviderWarning(
                    ProviderWarningCode.MissingMetric,
                    $"Metric '{metricId}' is missing from the Vercel AI Gateway report."));
                return;
            }
        }

        long sum = 0L;
        for (var i = 0; i < rows.Count; i++)
        {
            sum = checked(sum + selector(rows[i])!.Value);
        }

        metrics.Add(new ScalarMetricSnapshot(
            new MetricId(metricId),
            sum,
            unit,
            Provenance));
    }

    private static ProviderSnapshot CreateSnapshot(
        DateTimeOffset fetchedAtUtc,
        IEnumerable<MetricSnapshot> metrics,
        CoverageKind coverage)
    {
        return new ProviderSnapshot(
            new ProviderId(VercelGatewayProviderRuntime.ProviderIdValue),
            VercelGatewayProviderRuntime.DisplayNameValue,
            planLabel: null,
            fetchedAtUtc: fetchedAtUtc,
            sourceObservedAtUtc: fetchedAtUtc,
            timeZoneId: TimeZoneId,
            metrics: metrics,
            coverage: coverage,
            adapterContractVersion: AdapterContractVersion);
    }
}
