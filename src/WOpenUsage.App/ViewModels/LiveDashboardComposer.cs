using WOpenUsage.App.ViewModels.Sample;

namespace WOpenUsage.App.ViewModels;

public static class LiveDashboardComposer
{
    public static SampleDashboardSnapshot Create(
        IReadOnlyList<SampleProviderCard> providers,
        LocalUsageCard? localUsage,
        IReadOnlyList<SampleSpendSlice> additionalSpendSlices,
        string fallbackPeriodLabel,
        Func<IReadOnlyList<SampleSpendSlice>, DashboardSpendSummary> spendSummaryFormatter)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(additionalSpendSlices);
        ArgumentNullException.ThrowIfNull(fallbackPeriodLabel);
        ArgumentNullException.ThrowIfNull(spendSummaryFormatter);

        LocalUsageSpendBreakdown? spend = localUsage?.SpendBreakdown;
        SampleSpendSlice[] slices = (spend?.AgentSlices ?? [])
            .Concat(additionalSpendSlices.Where(additional =>
                !(spend?.AgentSlices ?? []).Any(local => string.Equals(
                    local.ProviderId,
                    additional.ProviderId,
                    StringComparison.Ordinal))))
            .ToArray();
        bool hasSpend = slices.Length > 0;
        DashboardSpendSummary summary = hasSpend
            ? spendSummaryFormatter(slices)
            : new DashboardSpendSummary(string.Empty, string.Empty, string.Empty);

        return new SampleDashboardSnapshot(
            SampleScenario.Normal,
            summary.TotalAmount,
            hasSpend && !string.IsNullOrWhiteSpace(localUsage?.PeriodLabel)
                ? localUsage!.PeriodLabel
                : fallbackPeriodLabel,
            summary.AccessibleName,
            slices,
            providers,
            summary.CompactTotalAmount);
    }
}
