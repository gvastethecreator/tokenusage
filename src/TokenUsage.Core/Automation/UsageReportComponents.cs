using TokenUsage.Core.Usage;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Automation;

public sealed record UsageComponentSummary(long MeasuredTokens, int MeasuredRecords,
    int UnknownRecords, int UnavailableRecords, int MissingDetailRecords);

public sealed record UsageReportComponents(UsageComponentSummary Input, UsageComponentSummary Output,
    UsageComponentSummary Reasoning, UsageComponentSummary CacheRead, UsageComponentSummary CacheWrite);

public sealed partial class UsageReportQuery
{
    public static UsageReportMetrics CombineMetrics(IEnumerable<UsageReportMetrics> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        UsageReportMetrics[] rows = source.ToArray();
        decimal? Cost(Func<UsageReportMetrics, decimal?> select) => rows.Any(row => select(row).HasValue)
            ? rows.Sum(row => select(row) ?? 0m) : null;
        return new(rows.Sum(row => row.EventCount),
            new(rows.Sum(row => row.Tokens.Input), rows.Sum(row => row.Tokens.Output),
                rows.Sum(row => row.Tokens.Reasoning), rows.Sum(row => row.Tokens.CacheRead),
                rows.Sum(row => row.Tokens.CacheWrite)),
            Cost(row => row.ReportedCostUsd), Cost(row => row.EstimatedCostUsd),
            rows.Sum(row => row.UnpricedTokens), rows.Sum(row => row.UnavailableCostEventCount),
            rows.Aggregate(CoverageKind.Complete,
                (coverage, row) => CoverageAggregation.Worst(coverage, row.Coverage)));
    }

    public static UsageReportComponents Components(UsageReport report,
        UsageReportSelection? selection = null, DateOnly? date = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        UsageReport selected = selection is null ? report : Select(report, selection);
        int expectedRecords = date is null ? selected.Totals.EventCount
            : selected.Days.Where(row => row.Date == date).Sum(row => row.Metrics.EventCount);
        return AggregateComponents(selected.ConfigurationRollups
            .Where(row => date is null || row.Usage.Date == date)
            .Select(row => (row.Usage.Tokens, row.DetailMetadata, row.Usage.EventCount)), expectedRecords);
    }

    public static UsageReportComponents Components(IEnumerable<UsageOverviewObservation> observations,
        int expectedRecordCount)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return AggregateComponents(observations.Select(row => (row.Tokens, row.DetailMetadata, 1)),
            expectedRecordCount);
    }

    private static UsageReportComponents AggregateComponents(
        IEnumerable<(TokenBreakdown Tokens, UsageDetailMetadata Detail, int Records)> observations,
        int expectedRecordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRecordCount);
        var input = new ComponentAccumulator();
        var output = new ComponentAccumulator();
        var reasoning = new ComponentAccumulator();
        var cacheRead = new ComponentAccumulator();
        var cacheWrite = new ComponentAccumulator();
        foreach (var row in observations)
        {
            input.Add(row.Tokens.Input, row.Detail.Input, row.Records);
            output.Add(row.Tokens.Output, row.Detail.Output, row.Records);
            reasoning.Add(row.Tokens.Reasoning, row.Detail.Reasoning, row.Records);
            cacheRead.Add(row.Tokens.CacheRead, row.Detail.CacheRead, row.Records);
            cacheWrite.Add(row.Tokens.CacheWrite, row.Detail.CacheWrite, row.Records);
        }
        return new(input.Summary(expectedRecordCount), output.Summary(expectedRecordCount),
            reasoning.Summary(expectedRecordCount), cacheRead.Summary(expectedRecordCount),
            cacheWrite.Summary(expectedRecordCount));
    }

    private sealed class ComponentAccumulator
    {
        private long _tokens;
        private int _measured, _unknown, _unavailable;

        public void Add(long tokens, UsageComponentAvailability availability, int records)
        {
            if (availability == UsageComponentAvailability.Measured)
            {
                _tokens = checked(_tokens + tokens);
                _measured = checked(_measured + records);
            }
            else if (availability == UsageComponentAvailability.Unavailable)
                _unavailable = checked(_unavailable + records);
            else _unknown = checked(_unknown + records);
        }

        public UsageComponentSummary Summary(int expectedRecordCount) => new(_tokens, _measured,
            _unknown, _unavailable, Math.Max(0, expectedRecordCount - _measured - _unknown - _unavailable));
    }
}
