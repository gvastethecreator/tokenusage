using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Codex;

internal static class CodexUsageSnapshotMapper
{
    private const string AdapterVersion = "codex-app-server/1";

    public static ProviderSnapshot AppendUsage(
        ProviderSnapshot quotaSnapshot,
        CodexTokenUsageSnapshot usage,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(quotaSnapshot);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (!string.Equals(
            quotaSnapshot.ProviderId.Value,
            "codex",
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Codex usage can only enrich a Codex snapshot.",
                nameof(quotaSnapshot));
        }

        CodexLocalUsageTotals totals = CodexDailyUsageAggregator.Aggregate(
            usage,
            quotaSnapshot.SourceObservedAtUtc,
            timeZone);
        var metrics = new List<MetricSnapshot>(quotaSnapshot.Metrics);
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.Derived,
            AdapterVersion);

        AddMetric(metrics, "usage.tokens.today", totals.TodayTokens, provenance);
        AddMetric(metrics, "usage.tokens.yesterday", totals.YesterdayTokens, provenance);
        AddMetric(metrics, "usage.tokens.7d", totals.Last7DaysTokens, provenance);
        AddMetric(metrics, "usage.tokens.30d", totals.Last30DaysTokens, provenance);

        return Copy(quotaSnapshot, metrics, quotaSnapshot.Coverage);
    }

    public static ProviderSnapshot MarkUsageUnavailable(ProviderSnapshot quotaSnapshot)
    {
        ArgumentNullException.ThrowIfNull(quotaSnapshot);
        return Copy(quotaSnapshot, quotaSnapshot.Metrics, CoverageKind.Partial);
    }

    private static void AddMetric(
        List<MetricSnapshot> metrics,
        string id,
        long? value,
        DataProvenance provenance)
    {
        if (value is null)
        {
            return;
        }

        metrics.Add(
            new ScalarMetricSnapshot(
                new MetricId(id),
                value.Value,
                "tokens",
                provenance));
    }

    private static ProviderSnapshot Copy(
        ProviderSnapshot source,
        IEnumerable<MetricSnapshot> metrics,
        CoverageKind coverage) =>
        new(
            source.ProviderId,
            source.DisplayName,
            source.PlanLabel,
            source.FetchedAtUtc,
            source.SourceObservedAtUtc,
            source.TimeZoneId,
            metrics,
            coverage,
            source.AdapterContractVersion);
}
