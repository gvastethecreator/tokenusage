using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;

namespace TokenUsage.App.ViewModels;

public static class LiveDashboardComposer
{
    public static DashboardSnapshot Create(
        IReadOnlyList<ProviderCard> providers,
        LocalUsageCard? localUsage,
        string fallbackPeriodLabel,
        Func<IReadOnlyList<SpendSlice>, DashboardSpendSummary> spendSummaryFormatter)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(fallbackPeriodLabel);
        ArgumentNullException.ThrowIfNull(spendSummaryFormatter);

        LocalUsageSpendBreakdown? spend = localUsage?.SpendBreakdown;
        IReadOnlyList<SpendSlice> slices = spend?.AgentSlices ?? [];
        bool hasSpend = slices.Count > 0;
        DashboardSpendSummary summary = hasSpend
            ? spendSummaryFormatter(slices)
            : new DashboardSpendSummary(string.Empty, string.Empty, string.Empty);

        return new DashboardSnapshot(
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
