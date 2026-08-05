using System.ComponentModel;

using TokenUsage.App.ViewModels.Sample;

namespace TokenUsage.App.ViewModels.Dashboard;

// Live + sample dashboard projection models (neutral names).

public sealed record SpendSlice(
    string ProviderId,
    string ProviderName,
    double Amount,
    string AmountText,
    string? ColorHex = null,
    string CompactAmountText = "")
{
    public string LegendAmountText => string.IsNullOrWhiteSpace(CompactAmountText)
        ? AmountText
        : CompactAmountText;
}

public sealed record UsageHeatmapCell(
    DateOnly Date,
    int Level,
    long TotalTokens,
    int EventCount,
    string AutomationId,
    string AccessibleName)
{
    public bool HasActivity => Level > 0;
}

public sealed record UsageHeatmapModel(
    string Title,
    string Summary,
    string AccessibleName,
    string AutomationId,
    IReadOnlyList<UsageHeatmapCell> Cells)
{
    public static UsageHeatmapModel Empty { get; } = new("", "", "", "", []);

    public bool HasData => Cells.Count > 0;
}

public sealed record QuotaWindow(
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
    DateTimeOffset? ResetAtUtc = null,
    double? QuotaRemainingPercent = null)
{
    public bool IsWithinLimit => !IsNearLimit;

    public bool HasPace => !string.IsNullOrWhiteSpace(PaceText);

    public bool IsPaceWithinLimit => HasPace && !IsPaceBehind;

    public string PaceAutomationName => $"{Title}: {PaceText}";

    public bool HasHighlight => IsHighlighted && !string.IsNullOrWhiteSpace(HighlightLabel);

    public string DisplayAutomationName => HasHighlight
        ? $"{AutomationName}. {HighlightLabel}"
        : AutomationName;

    public double ColorRemainingPercent => QuotaRemainingPercent ?? RemainingPercent;
}

public sealed record DashboardMetric(
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

public sealed class DashboardMetricItem
{
    private DashboardMetricItem(QuotaWindow? window, DashboardMetric? metric)
    {
        Window = window;
        Metric = metric;
    }

    public QuotaWindow? Window { get; }

    public DashboardMetric? Metric { get; }

    public static DashboardMetricItem FromWindow(QuotaWindow window) =>
        new(window ?? throw new ArgumentNullException(nameof(window)), null);

    public static DashboardMetricItem FromMetric(DashboardMetric metric) =>
        new(null, metric ?? throw new ArgumentNullException(nameof(metric)));

    public bool IsQuotaWindow => Window is not null;

    public bool IsScalarMetric => Metric is not null;

    public string Title => Window?.Title ?? string.Empty;

    public double RemainingPercent => Window?.RemainingPercent ?? 0d;

    public double ColorRemainingPercent => Window?.ColorRemainingPercent ?? 0d;

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

public sealed record ProviderCard(
    string ProviderId,
    string AutomationId,
    string Name,
    string PlanLabel,
    string CapabilityLabel,
    string? NoticeText,
    IReadOnlyList<QuotaWindow> Windows,
    IReadOnlyList<DashboardMetric> Metrics,
    IReadOnlyList<DashboardMetric>? SecondaryMetrics = null,
    string SourceLabel = "",
    string SourceValue = "",
    string ObservedLabel = "",
    string ObservedValue = "",
    string DetailsTooltip = "",
    string DetailsAutomationName = "",
    bool IsHighlighted = false,
    string HighlightLabel = "",
    IReadOnlyList<QuotaWindow>? SecondaryWindows = null,
    IReadOnlyList<DashboardMetricItem>? OrderedPrimaryMetrics = null,
    IReadOnlyList<DashboardMetricItem>? OrderedOnDemandMetrics = null,
    string? ProviderColorHex = null,
    UsageHeatmapModel? ActivityHeatmap = null) : INotifyPropertyChanged
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
            if (HasHeatmap)
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(IsHeatmapExpanded)));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public IReadOnlyList<DashboardMetric> SecondaryMetricItems => SecondaryMetrics ?? [];

    public IReadOnlyList<QuotaWindow> SecondaryWindowItems => SecondaryWindows ?? [];

    public IReadOnlyList<DashboardMetricItem> PrimaryMetricItems =>
        OrderedPrimaryMetrics
        ?? Windows.Select(DashboardMetricItem.FromWindow)
            .Concat(Metrics.Select(DashboardMetricItem.FromMetric))
            .ToArray();

    public IReadOnlyList<DashboardMetricItem> OnDemandMetricItems =>
        OrderedOnDemandMetrics
        ?? SecondaryWindowItems.Select(DashboardMetricItem.FromWindow)
            .Concat(SecondaryMetricItems.Select(DashboardMetricItem.FromMetric))
            .ToArray();

    public bool HasSecondaryMetrics => SecondaryMetricItems.Count > 0;

    public bool HasSecondaryWindows => SecondaryWindowItems.Count > 0;

    public UsageHeatmapModel Heatmap => ActivityHeatmap ?? UsageHeatmapModel.Empty;

    public bool HasHeatmap => Heatmap.HasData;

    public bool IsHeatmapExpanded => IsOnDemandMetricsExpanded && HasHeatmap;

    public bool HasOnDemandMetrics => OnDemandMetricItems.Count > 0 || HasHeatmap;

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

public sealed record DashboardSnapshot(
    SampleScenario Scenario,
    string TotalSpendAmount,
    string PeriodLabel,
    string SpendAccessibleName,
    IReadOnlyList<SpendSlice> SpendSlices,
    IReadOnlyList<ProviderCard> Providers,
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
    IReadOnlyList<DashboardMetric> Metrics,
    IReadOnlyList<LocalUsagePeriodRow> OtherPeriods,
    LocalUsageSpendBreakdown SpendBreakdown,
    IReadOnlyList<ProviderStatusRow> ProviderStatuses,
    UsageHeatmapModel? ActivityHeatmap = null,
    bool IsNoticeImportant = false)
{
    public bool HasData => Metrics.Count > 0;

    public UsageHeatmapModel Heatmap => ActivityHeatmap ?? UsageHeatmapModel.Empty;

    public bool HasUsageDetails => SpendBreakdown.HasContent || Heatmap.HasData;

    public string ExpandedNoticeText => IsNoticeImportant ? string.Empty : NoticeText;
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
    IReadOnlyList<SpendSlice> AgentSlices,
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
