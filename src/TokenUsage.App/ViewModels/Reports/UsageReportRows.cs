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
    string Detail,
    string AutomationId = "");

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
    public string? ModelProviderId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string ReportedValueText { get; init; } = string.Empty;
    public string EstimatedValueText { get; init; } = string.Empty;
    public string UnpricedValueText { get; init; } = string.Empty;
    public string SessionCountText { get; init; } = string.Empty;
    public string IdentityText => ProviderName + " · " + HostName;
    public string AutomationName => $"{ModelName}, {ProviderName}, {HostName}, {CostText}, {Metrics.Tokens.Total:N0} tokens, {ReportedValueText}, {EstimatedValueText}, {UnpricedValueText}, {SessionCountText}, {ActiveDays} active days";
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

public sealed record UsageReportProjectOverviewRow(
    string Id,
    string Name,
    UsageReportMetrics Metrics,
    string TokensText,
    string ShareText,
    string SessionCountText,
    string ReportedValueText,
    string EstimatedValueText,
    bool IsUnassigned,
    bool IsOther,
    string? ProjectKey)
{
    public string AutomationName => $"{Name}, {TokensText}, {ShareText}, {SessionCountText}, {ReportedValueText}, {EstimatedValueText}";
}

public sealed record UsageReportCompareRow(
    string Metric,
    string LeftText,
    string RightText,
    string DeltaText,
    bool LeftIsBest = false,
    bool RightIsBest = false);

public sealed record UsageReportResetLogRow(
    string WhenText,
    string QuotaText,
    string ExpectedText,
    string ClassText,
    string EvidenceText);

public sealed record UsageReportResetLogFilter(string Id, string Name);

public sealed record UsageReportRateStep(string Model, string CatalogVersion, string RangeText);
