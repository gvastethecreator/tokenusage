using TokenUsage.Core.Automation;

namespace TokenUsage.Core.Usage;

public enum UsageComparisonPreset { LastCompleteWeek, CurrentWeek, RollingSevenDays, SelectedPeriod }

public sealed record UsageComparisonPeriods(DateOnly BaselineStart, DateOnly BaselineEnd,
    DateOnly CurrentStart, DateOnly CurrentEnd, bool HasElapsedDays)
{
    public static UsageComparisonPeriods Resolve(UsageComparisonPreset preset, DateOnly today,
        DateOnly selectedStart, DateOnly selectedEnd)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        ArgumentOutOfRangeException.ThrowIfLessThan(selectedEnd, selectedStart);
        DateOnly monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        return preset switch
        {
            UsageComparisonPreset.LastCompleteWeek => new(monday.AddDays(-14), monday.AddDays(-8), monday.AddDays(-7), monday.AddDays(-1), true),
            UsageComparisonPreset.CurrentWeek => new(monday.AddDays(-7), today.AddDays(-8), monday, today.AddDays(-1), today > monday),
            UsageComparisonPreset.RollingSevenDays => new(today.AddDays(-14), today.AddDays(-8), today.AddDays(-7), today.AddDays(-1), true),
            _ => new(selectedStart.AddDays(-(selectedEnd.DayNumber - selectedStart.DayNumber + 1)), selectedStart.AddDays(-1), selectedStart, selectedEnd, true),
        };
    }
}

public sealed record UsageNumericChange(decimal? Baseline, decimal? Current, decimal? Absolute, decimal? RelativePercent)
{
    public static UsageNumericChange Between(decimal? baseline, decimal? current) =>
        new(baseline, current, baseline is { } a && current is { } b ? b - a : null,
            baseline is > 0m && current is { } value ? (value - baseline.Value) / baseline.Value * 100m : null);
}

public sealed record UsageModelContribution(string AgentId, string ModelId,
    UsageNumericChange Tokens, UsageNumericChange Cost);

public enum UsageBestDirection { None, Lower, Higher }

public sealed record UsageCostChangeSplit(decimal? Volume, decimal? Mix, decimal? Rate);

public static class UsageComparison
{
    public static int? Winner(decimal? left, decimal? right, UsageBestDirection direction)
    {
        if (direction == UsageBestDirection.None || left is not { } a || right is not { } b || a == b)
            return null;
        bool rightWins = direction == UsageBestDirection.Lower ? b < a : b > a;
        return rightWins ? 1 : -1;
    }

    public static UsageCostChangeSplit SplitKnownCost(UsageReport baseline, UsageReport current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        decimal? costA = KnownCost(baseline.Totals);
        decimal? costB = KnownCost(current.Totals);
        long pricedA = baseline.Totals.Tokens.Total - baseline.Totals.UnpricedTokens;
        long pricedB = current.Totals.Tokens.Total - current.Totals.UnpricedTokens;
        if (costA is not { } a || costB is not { } b || pricedA <= 0)
            return new(null, null, null);
        decimal volume = a * ((decimal)pricedB / pricedA - 1m);
        if (pricedB <= 0)
            return new(volume, null, null);
        decimal rate = (b / pricedB - a / pricedA) * pricedB;
        return new(volume, b - a - volume - rate, rate);
    }

    public static bool ReloadsForCatalogDate(bool useReferencePrices, bool isRatesAxis) =>
        useReferencePrices || isRatesAxis;

    public static bool OverlaysSingleReferencePrice(
        bool useReferencePrices, bool isCyclesAxis, bool isRatesAxis) =>
        useReferencePrices && !isCyclesAxis && !isRatesAxis;

    public static string CatalogDateLabel(DateTimeOffset utc, IFormatProvider culture) =>
        utc.UtcDateTime.ToString("d", culture) + " UTC";

    public static decimal? KnownCost(UsageReportMetrics metrics) =>
        metrics.ReportedCostUsd is null && metrics.EstimatedCostUsd is null && metrics.Tokens.Total > 0
            ? null : metrics.TotalCostUsd;

    public static decimal? CostPerMillionPricedTokens(UsageReportMetrics metrics) =>
        metrics.Tokens.Total - metrics.UnpricedTokens is > 0 and var priced
            && KnownCost(metrics) is { } cost ? cost * 1_000_000m / priced : null;

    public static int ActiveDays(UsageReport report) => report.Days.Count(day => day.Metrics.Tokens.Total > 0);

    private static Dictionary<(string, string), UsageReportMetrics> ModelTotals(UsageReport report) =>
        report.ModelDays.GroupBy(row => (row.AgentId.Value, row.ModelId.Value))
            .ToDictionary(group => group.Key,
                group => UsageReportQuery.Aggregate(group.Select(row => UsageReportQuery.ToRollup(row, "projection"))));

    public static IReadOnlyList<UsageModelContribution> Contributions(UsageReport baseline, UsageReport current)
    {
        var left = ModelTotals(baseline);
        var right = ModelTotals(current);
        UsageReportMetrics empty = UsageReportQuery.Build([]).Totals;
        return left.Keys.Union(right.Keys).Select(key =>
        {
            UsageReportMetrics a = left.GetValueOrDefault(key) ?? empty;
            UsageReportMetrics b = right.GetValueOrDefault(key) ?? empty;
            return new UsageModelContribution(key.Item1, key.Item2,
                UsageNumericChange.Between(a.Tokens.Total, b.Tokens.Total),
                UsageNumericChange.Between(KnownCost(a), KnownCost(b)));
        }).OrderByDescending(row => Math.Abs(row.Cost.Absolute ?? 0m))
            .ThenBy(row => row.AgentId, StringComparer.Ordinal).ThenBy(row => row.ModelId, StringComparer.Ordinal).ToArray();
    }
}
