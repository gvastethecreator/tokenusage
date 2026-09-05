using TokenUsage.Core.Appearance;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed record ReportChartStyleOption(ReportChartStyle Value, string DisplayName)
{
    public override string ToString() => DisplayName;

    public UsageReportTrendDataset Preview => new(
        UsageReportMetric.Tokens,
        Enumerable.Range(0, 6).Select(index => new UsageReportTrendDay(new DateOnly(2026, 1, index + 1), "")).ToArray(),
        [
            new("preview-a", "", "", "#60A5FA", [2, 6, 3, 8, 4, 7]) { TimeValues = Enumerable.Range(0, 72).Select(index => (double)(index % 7 + 1)).ToArray() },
            new("preview-b", "", "", "#A78BFA", [1, 2, 4, 2, 3, 2]) { TimeValues = Enumerable.Range(0, 72).Select(index => (double)(index % 3 + 1)).ToArray() },
        ], Value);
}
