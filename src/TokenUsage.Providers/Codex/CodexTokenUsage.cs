using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace TokenUsage.Providers.Codex;

public sealed record CodexUsageDailyBucket
{
    public CodexUsageDailyBucket(DateOnly startDate, long tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        StartDate = startDate;
        Tokens = tokens;
    }

    public DateOnly StartDate { get; }

    public long Tokens { get; }
}

public sealed record CodexUsageSummary
{
    public CodexUsageSummary(
        long? currentStreakDays,
        long? lifetimeTokens,
        long? longestRunningTurnSeconds,
        long? longestStreakDays,
        long? peakDailyTokens)
    {
        RequireNonNegative(currentStreakDays, nameof(currentStreakDays));
        RequireNonNegative(lifetimeTokens, nameof(lifetimeTokens));
        RequireNonNegative(longestRunningTurnSeconds, nameof(longestRunningTurnSeconds));
        RequireNonNegative(longestStreakDays, nameof(longestStreakDays));
        RequireNonNegative(peakDailyTokens, nameof(peakDailyTokens));

        CurrentStreakDays = currentStreakDays;
        LifetimeTokens = lifetimeTokens;
        LongestRunningTurnSeconds = longestRunningTurnSeconds;
        LongestStreakDays = longestStreakDays;
        PeakDailyTokens = peakDailyTokens;
    }

    public long? CurrentStreakDays { get; }

    public long? LifetimeTokens { get; }

    public long? LongestRunningTurnSeconds { get; }

    public long? LongestStreakDays { get; }

    public long? PeakDailyTokens { get; }

    private static void RequireNonNegative(long? value, string paramName)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Usage summary values cannot be negative.");
        }
    }
}

public sealed record CodexTokenUsageSnapshot
{
    internal const int MaximumDailyBuckets = 400;

    public CodexTokenUsageSnapshot(
        CodexUsageSummary summary,
        IEnumerable<CodexUsageDailyBucket> dailyUsageBuckets)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        ArgumentNullException.ThrowIfNull(dailyUsageBuckets);

        CodexUsageDailyBucket[] buckets = dailyUsageBuckets.ToArray();
        if (buckets.Length > MaximumDailyBuckets)
        {
            throw new ArgumentException(
                $"Token usage cannot contain more than {MaximumDailyBuckets} daily buckets.",
                nameof(dailyUsageBuckets));
        }

        if (buckets.Any(bucket => bucket is null))
        {
            throw new ArgumentException(
                "Token usage cannot contain null daily buckets.",
                nameof(dailyUsageBuckets));
        }

        DailyUsageBuckets = new ReadOnlyCollection<CodexUsageDailyBucket>(buckets);
    }

    public CodexUsageSummary Summary { get; }

    public IReadOnlyList<CodexUsageDailyBucket> DailyUsageBuckets { get; }
}

internal static class CodexTokenUsageParser
{
    public static CodexTokenUsageSnapshot Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("summary", out JsonElement summaryElement)
            || summaryElement.ValueKind != JsonValueKind.Object)
        {
            throw ContractFailure();
        }

        CodexUsageSummary summary = ParseSummary(summaryElement);
        var buckets = new List<CodexUsageDailyBucket>();

        if (result.TryGetProperty("dailyUsageBuckets", out JsonElement bucketsElement)
            && bucketsElement.ValueKind != JsonValueKind.Null)
        {
            if (bucketsElement.ValueKind != JsonValueKind.Array)
            {
                throw ContractFailure();
            }

            foreach (JsonElement bucketElement in bucketsElement.EnumerateArray())
            {
                if (buckets.Count >= CodexTokenUsageSnapshot.MaximumDailyBuckets)
                {
                    throw ContractFailure();
                }

                buckets.Add(ParseBucket(bucketElement));
            }
        }

        return new CodexTokenUsageSnapshot(summary, buckets);
    }

    private static CodexUsageSummary ParseSummary(JsonElement summary) =>
        new(
            ParseOptionalNonNegativeInt64(summary, "currentStreakDays"),
            ParseOptionalNonNegativeInt64(summary, "lifetimeTokens"),
            ParseOptionalNonNegativeInt64(summary, "longestRunningTurnSec"),
            ParseOptionalNonNegativeInt64(summary, "longestStreakDays"),
            ParseOptionalNonNegativeInt64(summary, "peakDailyTokens"));

    private static CodexUsageDailyBucket ParseBucket(JsonElement bucket)
    {
        if (bucket.ValueKind != JsonValueKind.Object
            || !bucket.TryGetProperty("startDate", out JsonElement startDateElement)
            || startDateElement.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(
                startDateElement.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly startDate)
            || !bucket.TryGetProperty("tokens", out JsonElement tokensElement)
            || tokensElement.ValueKind != JsonValueKind.Number
            || !tokensElement.TryGetInt64(out long tokens)
            || tokens < 0)
        {
            throw ContractFailure();
        }

        return new CodexUsageDailyBucket(startDate, tokens);
    }

    private static long? ParseOptionalNonNegativeInt64(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long parsed)
            || parsed < 0)
        {
            throw ContractFailure();
        }

        return parsed;
    }

    private static CodexProtocolException ContractFailure() =>
        new("Codex app-server returned an unsupported token-usage response.");
}
