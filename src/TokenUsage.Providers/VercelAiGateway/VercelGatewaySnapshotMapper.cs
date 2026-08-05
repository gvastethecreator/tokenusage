using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.VercelAiGateway;

internal static class VercelGatewaySnapshotMapper
{
    internal const string AdapterVersion = "vercel-ai-gateway-report/1";
    internal const string QuotaAdapterVersion = "vercel-ai-gateway-quota/1";
    internal const string QuotaStateAdapterVersion = "vercel-ai-gateway-quota-state/1";
    internal const int AdapterContractVersion = 2;
    internal const string TimeZoneId = "UTC";

    private static readonly DataProvenance Provenance = new DataProvenance(
        SourceKind.ManualKey,
        MeasurementKind.ProviderReported,
        AdapterVersion);

    private static readonly DataProvenance QuotaProvenance = new DataProvenance(
        SourceKind.ManualKey,
        MeasurementKind.ProviderReported,
        QuotaAdapterVersion);

    private static readonly DataProvenance QuotaStateProvenance = new DataProvenance(
        SourceKind.ManualKey,
        MeasurementKind.Derived,
        QuotaStateAdapterVersion);

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

    internal static MapResult Map(
        VercelGatewayReport report,
        DateTimeOffset fetchedAtUtc) =>
        Map(
            report,
            quotaResult: null,
            ProviderCapabilityState.NotRequested,
            fetchedAtUtc);

    internal static MapResult Map(
        VercelGatewayReport report,
        VercelGatewayQuotaLookupResult? quotaResult,
        ProviderCapabilityState quotaState,
        DateTimeOffset fetchedAtUtc,
        ProviderWarning? supplementalWarning = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var rows = report.Results;
        var metrics = new List<MetricSnapshot>();
        var warnings = new List<ProviderWarning>();
        var reportWarnings = new List<ProviderWarning>();
        if (supplementalWarning is not null)
        {
            warnings.Add(supplementalWarning);
        }

        AddQuotaMetric(metrics, quotaResult);
        ProviderCapabilitySnapshot[] capabilities =
        [
            new(
                new CapabilityId("quota.gateway.key.budget"),
                quotaState,
                quotaState is ProviderCapabilityState.Available
                    or ProviderCapabilityState.NotConfigured
                    ? QuotaProvenance
                    : QuotaStateProvenance),
        ];
        if (rows.Count == 0)
        {
            return new MapResult(
                CreateSnapshot(
                    fetchedAtUtc,
                    metrics,
                    CoverageKind.Complete,
                    capabilities),
                warnings.AsReadOnly());
        }

        TryAddDecimalMetric(
            metrics,
            reportWarnings,
            rows,
            "spend.gateway.total.30d",
            "usd",
            static r => r.TotalCost);

        TryAddDecimalMetric(
            metrics,
            reportWarnings,
            rows,
            "spend.gateway.market.30d",
            "usd",
            static r => r.MarketCost);

        TryAddDecimalMetric(
            metrics,
            reportWarnings,
            rows,
            "spend.gateway.surcharge.30d",
            "usd",
            static r => r.SurchargeCost);

        TryAddDecimalMetric(
            metrics,
            reportWarnings,
            rows,
            "spend.gateway.fee.30d",
            "usd",
            static r => r.GatewayCost);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.tokens.input.30d",
            "tokens",
            static r => r.InputTokens);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.tokens.output.30d",
            "tokens",
            static r => r.OutputTokens);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.tokens.cached-input.30d",
            "tokens",
            static r => r.CachedInputTokens);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.tokens.cache-creation-input.30d",
            "tokens",
            static r => r.CacheCreationInputTokens);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.tokens.reasoning.30d",
            "tokens",
            static r => r.ReasoningTokens);

        TryAddLongMetric(
            metrics,
            reportWarnings,
            rows,
            "usage.requests.30d",
            "requests",
            static r => r.RequestCount);

        warnings.AddRange(reportWarnings);
        var coverage = reportWarnings.Count > 0 ? CoverageKind.Partial : CoverageKind.Complete;
        return new MapResult(
            CreateSnapshot(fetchedAtUtc, metrics, coverage, capabilities),
            warnings);
    }

    private static void AddQuotaMetric(
        List<MetricSnapshot> metrics,
        VercelGatewayQuotaLookupResult? quotaResult)
    {
        if (quotaResult is not VercelGatewayQuotaLookupResult.Found found)
        {
            return;
        }

        metrics.Add(new ProgressMetricSnapshot(
            new MetricId("quota.gateway.key.budget"),
            found.Quota.CurrentSpend,
            found.Quota.LimitAmount,
            resetsAtUtc: null,
            QuotaProvenance,
            "usd",
            MapCadence(found.Quota.RefreshPeriod),
            found.Quota.Active));
    }

    private static ProgressResetCadence MapCadence(
        VercelGatewayQuotaRefreshPeriod refreshPeriod) => refreshPeriod switch
        {
            VercelGatewayQuotaRefreshPeriod.Daily => ProgressResetCadence.Daily,
            VercelGatewayQuotaRefreshPeriod.Weekly => ProgressResetCadence.Weekly,
            VercelGatewayQuotaRefreshPeriod.Monthly => ProgressResetCadence.Monthly,
            VercelGatewayQuotaRefreshPeriod.None => ProgressResetCadence.Never,
            _ => throw new ArgumentOutOfRangeException(nameof(refreshPeriod)),
        };

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
        CoverageKind coverage,
        IEnumerable<ProviderCapabilitySnapshot> capabilities)
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
            adapterContractVersion: AdapterContractVersion,
            capabilities: capabilities);
    }
}
