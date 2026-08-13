using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

internal static class ReportJson
{
    internal const string SchemaVersion = "tokenusage.report.v1";
    internal const int HighestCostDayCount = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static string Serialize(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        UsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(
            new ReportJsonDocument(
                SchemaVersion,
                generatedAt,
                new ReportJsonRange(fromInclusive, toInclusive, days),
                new ReportJsonFilter(agentId?.Value),
                CreateMetrics(report.Totals),
                report.Agents.Select(item => new ReportJsonAgent(
                    item.AgentId.Value,
                    CreateMetrics(item.Metrics))).ToArray(),
                report.Models.Select(item => new ReportJsonModel(
                    item.AgentId.Value,
                    item.ModelProviderId?.Value,
                    item.ModelId.Value,
                    CreateMetrics(item.Metrics))).ToArray(),
                report.Days
                    .OrderByDescending(item => item.Metrics.TotalCostUsd)
                    .ThenByDescending(item => item.Metrics.Tokens.Total)
                    .ThenBy(item => item.Date)
                    .Take(HighestCostDayCount)
                    .Select(CreateDay)
                    .ToArray(),
                report.Days.Select(CreateDay).ToArray()),
            SerializerOptions);
    }

    internal static string CoverageName(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => "complete",
        CoverageKind.Partial => "partial",
        CoverageKind.SummaryOnly => "summary-only",
        CoverageKind.Unpriced => "unpriced",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };

    private static ReportJsonMetrics CreateMetrics(UsageReportMetrics metrics) =>
        new(
            metrics.EventCount,
            new ReportJsonTokens(
                metrics.Tokens.Input,
                metrics.Tokens.Output,
                metrics.Tokens.Reasoning,
                metrics.Tokens.CacheRead,
                metrics.Tokens.CacheWrite,
                metrics.Tokens.Total,
                metrics.UnpricedTokens),
            new ReportJsonCost(
                metrics.TotalCostUsd,
                metrics.ReportedCostUsd,
                metrics.EstimatedCostUsd),
            metrics.UnavailableCostEventCount,
            CoverageName(metrics.Coverage),
            metrics.PriceCoveragePercent);

    private static ReportJsonDay CreateDay(UsageDayReport item) =>
        new(item.Date, CreateMetrics(item.Metrics));

    private sealed record ReportJsonDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        ReportJsonRange Range,
        ReportJsonFilter Filter,
        ReportJsonMetrics Totals,
        IReadOnlyList<ReportJsonAgent> ByAgent,
        IReadOnlyList<ReportJsonModel> Models,
        IReadOnlyList<ReportJsonDay> HighestCostDays,
        IReadOnlyList<ReportJsonDay> Daily);

    private sealed record ReportJsonRange(DateOnly From, DateOnly To, int Days);

    private sealed record ReportJsonFilter(string? Agent);

    private sealed record ReportJsonAgent(string Agent, ReportJsonMetrics Metrics);

    private sealed record ReportJsonModel(
        string Agent,
        string? Provider,
        string Model,
        ReportJsonMetrics Metrics);

    private sealed record ReportJsonDay(DateOnly Date, ReportJsonMetrics Metrics);

    private sealed record ReportJsonMetrics(
        int Events,
        ReportJsonTokens Tokens,
        ReportJsonCost CostUsd,
        int UnavailableCostEvents,
        string Coverage,
        decimal PriceCoveragePercent);

    private sealed record ReportJsonTokens(
        long Input,
        long Output,
        long Reasoning,
        long CacheRead,
        long CacheWrite,
        long Total,
        long Unpriced);

    private sealed record ReportJsonCost(
        decimal Total,
        decimal? Reported,
        decimal? Estimated);
}
