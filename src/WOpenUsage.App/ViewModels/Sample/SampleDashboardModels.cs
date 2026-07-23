using System.ComponentModel;

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
    string PaceAutomationId = "",
    string LayoutMetricId = "",
    bool IsHighlighted = false,
    string HighlightLabel = "",
    DateTimeOffset? ResetAtUtc = null)
{
    public bool IsWithinLimit => !IsNearLimit;

    public bool HasPace => !string.IsNullOrWhiteSpace(PaceText);

    public bool IsPaceWithinLimit => HasPace && !IsPaceBehind;

    public string PaceAutomationName => $"{Title}: {PaceText}";

    public bool HasHighlight => IsHighlighted && !string.IsNullOrWhiteSpace(HighlightLabel);

    public string DisplayAutomationName => HasHighlight
        ? $"{AutomationName}. {HighlightLabel}"
        : AutomationName;
}

public sealed record SampleMetric(
    string Label,
    string Value,
    string AutomationId = "",
    string LayoutMetricId = "",
    bool IsHighlighted = false,
    string HighlightLabel = "")
{
    public bool HasHighlight => IsHighlighted && !string.IsNullOrWhiteSpace(HighlightLabel);

    public string AutomationName => HasHighlight
        ? $"{Label}: {Value}. {HighlightLabel}"
        : $"{Label}: {Value}";
}

public sealed class SampleDashboardMetricItem
{
    private SampleDashboardMetricItem(SampleQuotaWindow? window, SampleMetric? metric)
    {
        Window = window;
        Metric = metric;
    }

    public SampleQuotaWindow? Window { get; }

    public SampleMetric? Metric { get; }

    public static SampleDashboardMetricItem FromWindow(SampleQuotaWindow window) =>
        new(window ?? throw new ArgumentNullException(nameof(window)), null);

    public static SampleDashboardMetricItem FromMetric(SampleMetric metric) =>
        new(null, metric ?? throw new ArgumentNullException(nameof(metric)));

    public bool IsQuotaWindow => Window is not null;

    public bool IsScalarMetric => Metric is not null;

    public string Title => Window?.Title ?? string.Empty;

    public double RemainingPercent => Window?.RemainingPercent ?? 0d;

    public string RemainingText => Window?.RemainingText ?? string.Empty;

    public string ResetText => Window?.ResetText ?? string.Empty;

    public string WindowAutomationName => Window?.DisplayAutomationName ?? string.Empty;

    public bool IsWithinLimit => Window?.IsWithinLimit ?? false;

    public bool IsNearLimit => Window?.IsNearLimit ?? false;

    public string PaceText => Window?.PaceText ?? string.Empty;

    public string PaceAutomationId => Window?.PaceAutomationId ?? string.Empty;

    public string PaceAutomationName => Window?.PaceAutomationName ?? string.Empty;

    public bool HasPace => Window?.HasPace ?? false;

    public string Label => Metric?.Label ?? string.Empty;

    public string Value => Metric?.Value ?? string.Empty;

    public string MetricAutomationId => Metric?.AutomationId ?? string.Empty;

    public string MetricAutomationName => Metric?.AutomationName ?? string.Empty;
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
    string DetailsAutomationName = "",
    bool IsHighlighted = false,
    string HighlightLabel = "",
    IReadOnlyList<SampleQuotaWindow>? SecondaryWindows = null,
    IReadOnlyList<SampleDashboardMetricItem>? OrderedPrimaryMetrics = null,
    IReadOnlyList<SampleDashboardMetricItem>? OrderedOnDemandMetrics = null) : INotifyPropertyChanged
{
    private bool _isOnDemandMetricsExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOnDemandMetricsExpanded
    {
        get => _isOnDemandMetricsExpanded;
        set
        {
            if (_isOnDemandMetricsExpanded == value)
            {
                return;
            }

            _isOnDemandMetricsExpanded = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsOnDemandMetricsExpanded)));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public IReadOnlyList<SampleMetric> SecondaryMetricItems => SecondaryMetrics ?? [];

    public IReadOnlyList<SampleQuotaWindow> SecondaryWindowItems => SecondaryWindows ?? [];

    public IReadOnlyList<SampleDashboardMetricItem> PrimaryMetricItems =>
        OrderedPrimaryMetrics
        ?? Windows.Select(SampleDashboardMetricItem.FromWindow)
            .Concat(Metrics.Select(SampleDashboardMetricItem.FromMetric))
            .ToArray();

    public IReadOnlyList<SampleDashboardMetricItem> OnDemandMetricItems =>
        OrderedOnDemandMetrics
        ?? SecondaryWindowItems.Select(SampleDashboardMetricItem.FromWindow)
            .Concat(SecondaryMetricItems.Select(SampleDashboardMetricItem.FromMetric))
            .ToArray();

    public bool HasSecondaryMetrics => SecondaryMetricItems.Count > 0;

    public bool HasSecondaryWindows => SecondaryWindowItems.Count > 0;

    public bool HasOnDemandMetrics => OnDemandMetricItems.Count > 0;

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(SourceValue) || !string.IsNullOrWhiteSpace(ObservedValue);

    public string DetailsAutomationId => $"{AutomationId}.Details";

    public string DetailsSourceAutomationId => $"{AutomationId}.Details.Source";

    public string DetailsObservedAutomationId => $"{AutomationId}.Details.Observed";

    public string SecondaryMetricsAutomationId => $"{AutomationId}.SecondaryMetrics";

    public bool HasHighlight => IsHighlighted && !string.IsNullOrWhiteSpace(HighlightLabel);

    public string CardAutomationName => HasHighlight
        ? $"{Name}. {HighlightLabel}"
        : Name;
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
