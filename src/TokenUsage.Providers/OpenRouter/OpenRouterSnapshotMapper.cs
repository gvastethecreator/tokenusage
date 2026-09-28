using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.OpenRouter;

/// <summary>
/// Maps <c>GET /api/v1/key</c> to a snapshot. OpenRouter credits are USD, so each spend value
/// is in USD. The day, week, and month values are UTC calendar periods (the week starts on
/// Monday), not rolling windows.
/// </summary>
internal static class OpenRouterSnapshotMapper
{
    internal const string AdapterVersion = "openrouter-key/1";
    internal const int AdapterContractVersion = 1;
    internal const string TimeZoneId = "UTC";

    internal const string KeyLimitMetricId = "quota.openrouter.key.limit";
    internal const string SpendDayMetricId = "spend.openrouter.day";
    internal const string SpendWeekMetricId = "spend.openrouter.week";
    internal const string SpendMonthMetricId = "spend.openrouter.month";
    internal const string SpendTotalMetricId = "spend.openrouter.total";
    internal const string FreeTierPlanLabel = "free";
    internal const string PaidPlanLabel = "paid";

    private static readonly DataProvenance Provenance = new(
        SourceKind.ManualKey,
        MeasurementKind.ProviderReported,
        AdapterVersion);

    internal static ProviderSnapshot Map(OpenRouterKeyUsage usage, DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var metrics = new List<MetricSnapshot>
        {
            Spend(SpendDayMetricId, usage.DailyUsage),
            Spend(SpendWeekMetricId, usage.WeeklyUsage),
            Spend(SpendMonthMetricId, usage.MonthlyUsage),
            Spend(SpendTotalMetricId, usage.Usage),
        };

        bool hasLimit = usage.Limit is > 0m;
        if (hasLimit)
        {
            decimal limit = usage.Limit!.Value;
            ProgressResetCadence? cadence = usage.LimitReset switch
            {
                null => ProgressResetCadence.Never,
                OpenRouterLimitReset.Daily => ProgressResetCadence.Daily,
                OpenRouterLimitReset.Weekly => ProgressResetCadence.Weekly,
                OpenRouterLimitReset.Monthly => ProgressResetCadence.Monthly,
                _ => null,
            };
            metrics.Add(new ProgressMetricSnapshot(
                new MetricId(KeyLimitMetricId),
                UsedAgainstLimit(usage, limit),
                limit,
                UtcResetSchedule.Next(cadence, fetchedAtUtc),
                Provenance,
                "usd",
                cadence,
                isActive: true));
        }

        return new ProviderSnapshot(
            new ProviderId(OpenRouterProviderRuntime.ProviderIdValue),
            OpenRouterProviderRuntime.DisplayNameValue,
            usage.IsFreeTier ? FreeTierPlanLabel : PaidPlanLabel,
            fetchedAtUtc,
            fetchedAtUtc,
            TimeZoneId,
            metrics,
            CoverageKind.Complete,
            AdapterContractVersion,
            [
                new ProviderCapabilitySnapshot(
                    new CapabilityId(KeyLimitMetricId),
                    hasLimit
                        ? ProviderCapabilityState.Available
                        : ProviderCapabilityState.NotConfigured,
                    Provenance),
            ]);
    }

    /// <summary>
    /// OpenRouter reports what is left of the limit. Without that value, the spend of the
    /// period the limit resets on stands in for it.
    /// </summary>
    private static decimal UsedAgainstLimit(OpenRouterKeyUsage usage, decimal limit)
    {
        if (usage.LimitRemaining is decimal remaining)
        {
            return Math.Max(0m, limit - remaining);
        }

        return usage.LimitReset switch
        {
            OpenRouterLimitReset.Daily => usage.DailyUsage,
            OpenRouterLimitReset.Weekly => usage.WeeklyUsage,
            OpenRouterLimitReset.Monthly => usage.MonthlyUsage,
            _ => usage.Usage,
        };
    }

    private static ScalarMetricSnapshot Spend(string metricId, decimal value) =>
        new(new MetricId(metricId), value, "usd", Provenance);
}
