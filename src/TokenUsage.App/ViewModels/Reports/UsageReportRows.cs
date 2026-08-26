using Microsoft.UI.Xaml.Media;

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
    string ProviderId,
    string ProviderName,
    string ModelName,
    string CostText,
    string ShareText,
    string TokensText,
    string CoverageText);

public sealed record UsageReportDayRow(
    string DateText,
    string CostText,
    string TokensText,
    string EventsText,
    string CoverageText);

public sealed record UsageReportQualityRow(string Label, string Value);

public sealed record UsageReportSourceRow(
    string ProviderId,
    string Name,
    string ReportedCostText,
    string EstimatedCostText,
    string TokensText,
    string CoverageText);

public sealed record UsageReportCompareRow(
    string Metric,
    string LeftText,
    string RightText,
    string DeltaText);
