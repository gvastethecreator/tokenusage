namespace WOpenUsage.App.ViewModels.Sample;

public sealed record SampleSpendSlice(
    string ProviderId,
    string ProviderName,
    double Amount,
    string AmountText);

public sealed record SampleQuotaWindow(
    string Title,
    double RemainingPercent,
    string RemainingText,
    string ResetText,
    string AutomationName,
    bool IsNearLimit,
    string? PaceText = null,
    bool IsPaceBehind = false,
    string PaceAutomationId = "")
{
    public bool IsWithinLimit => !IsNearLimit;

    public bool HasPace => !string.IsNullOrWhiteSpace(PaceText);

    public bool IsPaceWithinLimit => HasPace && !IsPaceBehind;

    public string PaceAutomationName => $"{Title}: {PaceText}";
}

public sealed record SampleMetric(
    string Label,
    string Value,
    string AutomationId = "")
{
    public string AutomationName => $"{Label}: {Value}";
}

public sealed record SampleProviderCard(
    string ProviderId,
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
    string SpendAccessibleName,
    IReadOnlyList<SampleSpendSlice> SpendSlices,
    IReadOnlyList<SampleProviderCard> Providers)
{
    public bool HasSpend => SpendSlices.Count > 0;
}
