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
    IReadOnlyList<SampleMetric> Metrics,
    IReadOnlyList<SampleMetric>? SecondaryMetrics = null,
    string SourceLabel = "",
    string SourceValue = "",
    string ObservedLabel = "",
    string ObservedValue = "",
    string DetailsTooltip = "",
    string DetailsAutomationName = "")
{
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public IReadOnlyList<SampleMetric> SecondaryMetricItems => SecondaryMetrics ?? [];

    public bool HasSecondaryMetrics => SecondaryMetricItems.Count > 0;

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(SourceValue) || !string.IsNullOrWhiteSpace(ObservedValue);

    public string DetailsAutomationId => $"{AutomationId}.Details";

    public string DetailsSourceAutomationId => $"{AutomationId}.Details.Source";

    public string DetailsObservedAutomationId => $"{AutomationId}.Details.Observed";

    public string SecondaryMetricsAutomationId => $"{AutomationId}.SecondaryMetrics";
}

public sealed record SampleDashboardSnapshot(
    SampleScenario Scenario,
    string TotalSpendAmount,
    string PeriodLabel,
    string SpendAccessibleName,
    IReadOnlyList<SampleSpendSlice> SpendSlices,
    IReadOnlyList<SampleProviderCard> Providers,
    string CompactTotalSpendAmount = "")
{
    public bool HasSpend => SpendSlices.Count > 0;

    public string DonutCenterAmount => string.IsNullOrWhiteSpace(CompactTotalSpendAmount)
        ? TotalSpendAmount
        : CompactTotalSpendAmount;
}

public sealed record LocalUsageCard(
    string Title,
    string SourceLabel,
    string PeriodLabel,
    string NoticeText,
    IReadOnlyList<SampleMetric> Metrics,
    IReadOnlyList<LocalUsagePeriodRow> OtherPeriods,
    LocalUsageSpendBreakdown SpendBreakdown,
    IReadOnlyList<ProviderStatusRow> ProviderStatuses)
{
    public bool HasData => Metrics.Count > 0;
}

public sealed record ProviderCapabilityRow(
    string Label,
    string Value,
    string AutomationId)
{
    public string AutomationName => $"{Label}: {Value}";
}

public sealed record ProviderStatusRow(
    string ProviderId,
    string Name,
    string RootState,
    string RecoveryText,
    IReadOnlyList<ProviderCapabilityRow> Capabilities,
    string AutomationId)
{
    public string AutomationName => $"{Name}. {RootState}. {RecoveryText}";
}

public sealed record LocalUsagePeriodRow(
    string Label,
    string CostText,
    string DetailText,
    string AutomationId,
    string AutomationName);

public sealed record LocalUsageModelRow(
    string AgentId,
    string AgentName,
    string ModelName,
    string ReportedText,
    string EstimatedText,
    string CoverageText,
    string AutomationId,
    string Title,
    string AutomationName);

public sealed record LocalUsageSpendBreakdown(
    string Title,
    string SummaryText,
    string TotalText,
    string AccessibleName,
    IReadOnlyList<SampleSpendSlice> AgentSlices,
    IReadOnlyList<LocalUsageModelRow> Models,
    string CompactTotalText = "")
{
    public bool HasAgentSpend => AgentSlices.Count > 0;

    public bool HasModels => Models.Count > 0;

    public bool HasContent => HasAgentSpend || HasModels;

    public string DonutCenterText => string.IsNullOrWhiteSpace(CompactTotalText)
        ? TotalText
        : CompactTotalText;
}
