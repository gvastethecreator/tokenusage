namespace TokenUsage.Core.Usage;

public sealed record UsageSessionOutlier(
    UsageStatisticAvailability Availability,
    string Reason,
    string? SessionKey,
    long? SessionTokens,
    long? MedianTokens,
    long? MedianAbsoluteDeviation)
{
    public const string MethodVersion = "session-outlier/v1";
    public const int MinimumComparableSessions = 30;
    public const int FenceMultiplier = 6;

    public static UsageSessionOutlier Evaluate(IReadOnlyList<(string SessionKey, long Tokens)> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        (string SessionKey, long Tokens)[] comparable = sessions
            .Where(row => !string.IsNullOrWhiteSpace(row.SessionKey))
            .GroupBy(row => row.SessionKey, StringComparer.Ordinal)
            .Select(group => (SessionKey: group.Key, Tokens: group.Sum(item => item.Tokens)))
            .OrderBy(row => row.SessionKey, StringComparer.Ordinal)
            .ToArray();
        if (comparable.Length < MinimumComparableSessions)
        {
            return new UsageSessionOutlier(
                UsageStatisticAvailability.InsufficientSample,
                "Session outliers need 30 comparable eligible sessions.",
                null,
                null,
                null,
                null);
        }

        long[] totals = comparable.Select(row => row.Tokens).OrderBy(value => value).ToArray();
        long median = UsageDistributionEligibility.Median(totals);
        long mad = UsageDistributionEligibility.MedianAbsoluteDeviation(
            comparable.Select(row => row.Tokens).ToArray(),
            median);
        if (mad <= 0)
        {
            return new UsageSessionOutlier(
                UsageStatisticAvailability.Unavailable,
                "Session outliers need a positive median absolute deviation.",
                null,
                null,
                median,
                mad);
        }

        long fence = median + (FenceMultiplier * mad);
        (string SessionKey, long Tokens)? outlier = comparable
            .Where(row => row.Tokens > fence)
            .OrderByDescending(row => row.Tokens)
            .ThenBy(row => row.SessionKey, StringComparer.Ordinal)
            .Select(row => ((string SessionKey, long Tokens)?)row)
            .FirstOrDefault();
        if (outlier is null)
        {
            return new UsageSessionOutlier(
                UsageStatisticAvailability.Unavailable,
                "No comparable session is strictly above median plus 6 times MAD.",
                null,
                null,
                median,
                mad);
        }

        return new UsageSessionOutlier(
            UsageStatisticAvailability.Measured,
            MethodVersion,
            outlier.Value.SessionKey,
            outlier.Value.Tokens,
            median,
            mad);
    }
}
