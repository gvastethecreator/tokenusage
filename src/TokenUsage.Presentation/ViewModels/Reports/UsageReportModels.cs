using TokenUsage.App.Controls;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public enum UsageReportMetric
{
    Cost,
    Tokens,
    Share,
}

public sealed record UsageReportTrendDay(
    DateOnly Date,
    string Label,
    string? HoverText = null)
{
    public IReadOnlyList<UsageReportTrendReset> Resets { get; init; } = [];
    public string ResetText => string.Join("\n", Resets.Select(reset => reset.Text));
}

public sealed record UsageReportTrendSeries(
    string Id,
    string ProviderId,
    string Name,
    string ColorHex,
    IReadOnlyList<double> Values,
    string? ModelId = null)
{
    public IReadOnlyList<UsageTrendPointKind> PointKinds { get; init; } = [];

    public string? LegendName { get; init; }

    public bool IsReserve => ModelId == "gpt-reserve";
}

public sealed record UsageReportTrendDataset(
    UsageReportMetric Metric,
    IReadOnlyList<UsageReportTrendDay> Days,
    IReadOnlyList<UsageReportTrendSeries> Series,
    bool IsComparison = false,
    bool EmphasizeSmallValues = false)
{
    public string? UnavailableText { get; init; }
    public static UsageReportTrendDataset Empty { get; } = new(
        UsageReportMetric.Cost,
        [],
        []);

    /// <summary>
    /// True when both datasets would draw the same chart. Record equality compares list
    /// references, so a rebuilt but identical dataset would otherwise redraw and replay motion.
    /// </summary>
    public static bool HaveSameContent(UsageReportTrendDataset? left, UsageReportTrendDataset? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Metric == right.Metric && left.IsComparison == right.IsComparison
            && left.EmphasizeSmallValues == right.EmphasizeSmallValues && left.UnavailableText == right.UnavailableText
            && left.Days.SequenceEqual(right.Days, TrendDayComparer.Instance)
            && left.Series.SequenceEqual(right.Series, TrendSeriesComparer.Instance);
    }

    private sealed class TrendDayComparer : IEqualityComparer<UsageReportTrendDay>
    {
        public static readonly TrendDayComparer Instance = new();
        public bool Equals(UsageReportTrendDay? x, UsageReportTrendDay? y) =>
            x is not null && y is not null && x == y && x.Resets.SequenceEqual(y.Resets);
        public int GetHashCode(UsageReportTrendDay obj) => obj.Date.GetHashCode();
    }

    private sealed class TrendSeriesComparer : IEqualityComparer<UsageReportTrendSeries>
    {
        public static readonly TrendSeriesComparer Instance = new();
        public bool Equals(UsageReportTrendSeries? x, UsageReportTrendSeries? y) =>
            x is not null && y is not null
            && x.Id == y.Id && x.ProviderId == y.ProviderId && x.Name == y.Name && x.ColorHex == y.ColorHex
            && x.ModelId == y.ModelId && x.LegendName == y.LegendName
            && x.Values.SequenceEqual(y.Values)
            && x.PointKinds.SequenceEqual(y.PointKinds);
        public int GetHashCode(UsageReportTrendSeries obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
