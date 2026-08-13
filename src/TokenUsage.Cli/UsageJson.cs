using System.Text.Json;

namespace TokenUsage.Cli;

internal static class UsageJson
{
    internal const string SchemaVersion = "tokenusage.usage.v1";

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
        UsageCliSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(
            new UsageJsonDocument(
                SchemaVersion,
                generatedAt,
                new UsageJsonRange(fromInclusive, toInclusive, days),
                summary.EventCount,
                new UsageJsonTokens(summary.TotalTokens, summary.UnpricedTokens),
                new UsageJsonCost(summary.ReportedCostUsd, summary.EstimatedCostUsd)),
            SerializerOptions);
    }

    private sealed record UsageJsonDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        UsageJsonRange Range,
        int Events,
        UsageJsonTokens Tokens,
        UsageJsonCost CostUsd);

    private sealed record UsageJsonRange(DateOnly From, DateOnly To, int Days);

    private sealed record UsageJsonTokens(long Total, long Unpriced);

    private sealed record UsageJsonCost(decimal? Reported, decimal? Estimated);
}
