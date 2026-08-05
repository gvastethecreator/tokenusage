namespace TokenUsage.Providers.Codex;

public sealed record CodexLocalUsageTotals(
    DateOnly LocalToday,
    long? TodayTokens,
    long? YesterdayTokens,
    long? Last7DaysTokens,
    long? Last30DaysTokens);

public static class CodexDailyUsageAggregator
{
    public static CodexLocalUsageTotals Aggregate(
        CodexTokenUsageSnapshot source,
        DateTimeOffset observedAtUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Codex observation time must use the UTC offset.",
                nameof(observedAtUtc));
        }

        try
        {
            DateOnly today = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(observedAtUtc, timeZone).DateTime);
            DateOnly yesterday = today.AddDays(-1);
            DateOnly first7Day = today.AddDays(-6);
            DateOnly first30Day = today.AddDays(-29);
            var dates = new HashSet<DateOnly>();
            long? todayTokens = null;
            long? yesterdayTokens = null;
            long? last7DaysTokens = null;
            long? last30DaysTokens = null;

            foreach (CodexUsageDailyBucket bucket in source.DailyUsageBuckets)
            {
                if (!dates.Add(bucket.StartDate))
                {
                    throw MappingFailure();
                }

                if (bucket.StartDate > today)
                {
                    continue;
                }

                if (bucket.StartDate == today)
                {
                    todayTokens = Add(todayTokens, bucket.Tokens);
                }

                if (bucket.StartDate == yesterday)
                {
                    yesterdayTokens = Add(yesterdayTokens, bucket.Tokens);
                }

                if (bucket.StartDate >= first7Day)
                {
                    last7DaysTokens = Add(last7DaysTokens, bucket.Tokens);
                }

                if (bucket.StartDate >= first30Day)
                {
                    last30DaysTokens = Add(last30DaysTokens, bucket.Tokens);
                }
            }

            return new CodexLocalUsageTotals(
                today,
                todayTokens,
                yesterdayTokens,
                last7DaysTokens,
                last30DaysTokens);
        }
        catch (CodexProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw MappingFailure();
        }
    }

    private static long Add(long? current, long value) =>
        checked((current ?? 0) + value);

    private static CodexProtocolException MappingFailure() =>
        new("Codex daily usage could not be mapped safely.");
}
