namespace WOpenUsage.App.ViewModels.Sample;

public sealed record SampleSpendSlice(
    string ProviderName,
    string AmountText,
    double SharePercent);

public sealed record SampleQuotaWindow(
    string Title,
    double RemainingPercent,
    string RemainingText,
    string ResetText,
    string AutomationName,
    bool IsNearLimit)
{
    public bool IsWithinLimit => !IsNearLimit;
}

public sealed record SampleMetric(string Label, string Value);

public sealed record SampleProviderCard(
    string AutomationId,
    string Name,
    string PlanLabel,
    string CapabilityLabel,
    string? NoticeText,
    IReadOnlyList<SampleQuotaWindow> Windows,
    IReadOnlyList<SampleMetric> Metrics)
{
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);
}

public sealed record SampleDashboardSnapshot(
    SampleScenario Scenario,
    string TotalSpendAmount,
    string PeriodLabel,
    IReadOnlyList<SampleSpendSlice> SpendSlices,
    IReadOnlyList<SampleProviderCard> Providers);
