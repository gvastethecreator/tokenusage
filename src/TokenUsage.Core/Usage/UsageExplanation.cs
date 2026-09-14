using TokenUsage.Core.Automation;

namespace TokenUsage.Core.Usage;

public enum UsageExplanationMetric { Tokens, KnownCost }
public enum UsageExplanationIssue
{
    UnsupportedComparison, InvalidPeriod, UnequalPeriods, UnknownMethod, MethodChanged,
    TimeZonesDiffer, PriceCoverageChanged, DetailCoverageChanged, DetailCoverageUnknown,
    NoObservations, UnpricedCost, ModelTotalsDoNotReconcile,
}

public sealed record UsageExplanationResult(string RuleVersion, UsageComparisonDefinition Selection,
    UsageExplanationMetric Metric, IReadOnlyList<UsageExplanationIssue> Issues,
    decimal? TotalChange, IReadOnlyList<UsageModelContribution> LeadingModels, decimal? OtherModelsChange,
    decimal? PriceCoverageChangePoints, decimal? DetailCoverageChangePoints,
    UsageCacheComposition? BaselineCache, UsageCacheComposition? CurrentCache);

/// <summary>Descriptive arithmetic for the supplied snapshots; never inferred causality.</summary>
public static class UsageExplanation
{
    public static UsageExplanationResult Compare(UsageReport baseline, UsageReport current,
        UsageComparisonDefinition selection, UsageExplanationMetric metric)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        var issues = new List<UsageExplanationIssue>();
        bool comparable = true;
        void Block(UsageExplanationIssue issue) { issues.Add(issue); comparable = false; }
        if (selection.Axis is not ("Periods" or "Providers" or "Models")
            || baseline.IsExactInterval || current.IsExactInterval || !string.IsNullOrEmpty(selection.MethodId))
            Block(UsageExplanationIssue.UnsupportedComparison);
        int daysA = selection.BaselineEnd.DayNumber - selection.BaselineStart.DayNumber + 1;
        int daysB = selection.CurrentEnd.DayNumber - selection.CurrentStart.DayNumber + 1;
        if (daysA <= 0 || daysB <= 0) Block(UsageExplanationIssue.InvalidPeriod);
        else if (daysA != daysB) Block(UsageExplanationIssue.UnequalPeriods);
        if (baseline.ParserVersions.Count == 0 || current.ParserVersions.Count == 0)
            Block(UsageExplanationIssue.UnknownMethod);
        else if (!Same(baseline.ParserVersions, current.ParserVersions)) Block(UsageExplanationIssue.MethodChanged);
        if (!Same(baseline.GroupingTimeZoneIds, current.GroupingTimeZoneIds))
            Block(UsageExplanationIssue.TimeZonesDiffer);
        if (metric == UsageExplanationMetric.KnownCost && !Same(baseline.PricingVersions, current.PricingVersions))
            Block(UsageExplanationIssue.MethodChanged);

        decimal? priceA = PriceCoverage(baseline), priceB = PriceCoverage(current);
        decimal? priceChange = priceB - priceA;
        if (priceChange is { } price && Math.Abs(price) >= 10m)
            issues.Add(UsageExplanationIssue.PriceCoverageChanged);
        decimal? detailA = DetailCoverage(baseline), detailB = DetailCoverage(current);
        decimal? detailChange = detailB - detailA;
        if (detailChange is { } detail && Math.Abs(detail) >= 10m)
            issues.Add(UsageExplanationIssue.DetailCoverageChanged);
        else if (detailA is null || detailB is null) issues.Add(UsageExplanationIssue.DetailCoverageUnknown);
        if (baseline.Totals.EventCount == 0 || current.Totals.EventCount == 0)
            Block(UsageExplanationIssue.NoObservations);
        if (metric == UsageExplanationMetric.KnownCost
            && (baseline.Totals.UnpricedTokens > 0 || current.Totals.UnpricedTokens > 0
                || baseline.Totals.UnavailableCostEventCount > 0 || current.Totals.UnavailableCostEventCount > 0))
            Block(UsageExplanationIssue.UnpricedCost);

        decimal? total = null, remainder = null;
        UsageModelContribution[] leading = [];
        if (comparable)
        {
            decimal Value(UsageModelContribution row) => metric == UsageExplanationMetric.Tokens
                ? row.Tokens.Absolute!.Value : row.Cost.Absolute!.Value;
            total = metric == UsageExplanationMetric.Tokens
                ? (decimal)current.Totals.Tokens.Total - baseline.Totals.Tokens.Total
                : current.Totals.TotalCostUsd - baseline.Totals.TotalCostUsd;
            UsageModelContribution[] all = UsageComparison.Contributions(baseline, current).ToArray();
            bool Reconciles(UsageReport report) => report.ModelDays.Sum(row => row.Metrics.Tokens.Total) == report.Totals.Tokens.Total
                && (metric != UsageExplanationMetric.KnownCost
                    || report.ModelDays.Sum(row => row.Metrics.TotalCostUsd) == report.Totals.TotalCostUsd);
            if (!Reconciles(baseline) || !Reconciles(current)
                || all.Any(row => metric == UsageExplanationMetric.Tokens ? row.Tokens.Absolute is null : row.Cost.Absolute is null)
                || all.Sum(Value) != total)
            {
                issues.Add(UsageExplanationIssue.ModelTotalsDoNotReconcile);
                total = null;
            }
            else
            {
                leading = all.Where(row => Value(row) != 0).OrderByDescending(row => Math.Abs(Value(row)))
                    .ThenBy(row => row.AgentId, StringComparer.Ordinal).ThenBy(row => row.ModelId, StringComparer.Ordinal)
                    .Take(2).ToArray();
                remainder = total - leading.Sum(Value);
            }
        }
        return new("observed-change-explanations/v1", selection, metric, issues.Distinct().ToArray(),
            total, leading, remainder, priceChange, detailChange, baseline.CacheComposition, current.CacheComposition);
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.ToHashSet(StringComparer.Ordinal).SetEquals(b);

    private static decimal? PriceCoverage(UsageReport report) => report.Totals.Tokens.Total > 0
        ? 100m * (report.Totals.Tokens.Total - report.Totals.UnpricedTokens) / report.Totals.Tokens.Total : null;

    private static decimal? DetailCoverage(UsageReport report) => report.Totals.EventCount > 0
        && report.ActivityCoverage is { } coverage
        ? 100m * (report.Totals.EventCount - coverage.MissingDetailRecords) / report.Totals.EventCount : null;
}
