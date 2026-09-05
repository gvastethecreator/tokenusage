using Microsoft.UI.Xaml.Media;
using TokenUsage.Core.Automation;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportProviderRow(
    string ProviderId,
    string Name,
    string ValueText,
    string DetailText,
    double SharePercent,
    string ShareText,
    Brush AccentBrush,
    double CompositionWidth,
    UsageReportTrendDataset Trend);

public sealed record UsageReportMetricCard(
    string Label,
    string Value,
    string Detail);

public sealed record UsageReportModelRow(
    string Id,
    string ModelId,
    UsageReportMetrics Metrics,
    int ActiveDays,
    string ProviderId,
    string ProviderName,
    string ModelName,
    string CostText,
    string ShareText,
    string TokensText,
    string CoverageText)
{
    public bool IsReserve => ModelId == "gpt-reserve";
    public string AutomationName => $"{ModelName}, {ProviderName}, {CostText}, {TokensText}, {ActiveDays} active days";
}

public sealed record UsageReportDayRow(
    string Id,
    DateOnly Date,
    UsageReportMetrics Metrics,
    string DateText,
    string CostText,
    string TokensText,
    string EventsText,
    string CoverageText)
{
    public string AutomationName => $"{DateText}, {CostText}, {TokensText}, {EventsText}";
}

public sealed record UsageReportQualityRow(string Label, string Value);

public sealed record UsageReportSourceRow(
    string Id,
    UsageReportMetrics Metrics,
    int ActiveDays,
    string ProviderId,
    string Name,
    string ReportedCostText,
    string EstimatedCostText,
    string TokensText,
    string CoverageText)
{
    public string AutomationName => $"{Name}, {ReportedCostText}, {EstimatedCostText}, {TokensText}, {ActiveDays} active days";
}

public sealed record UsageReportCompareRow(
    string Metric,
    string LeftText,
    string RightText,
    string DeltaText);
