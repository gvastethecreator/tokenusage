using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexDailyUsageAggregatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CivilWindowsIncludeTheirExactBoundariesAndIgnoreFutureBuckets()
    {
        CodexTokenUsageSnapshot source = Snapshot(
            Bucket(2026, 7, 23, 999),
            Bucket(2026, 7, 22, 800),
            Bucket(2026, 7, 21, 400),
            Bucket(2026, 7, 16, 100),
            Bucket(2026, 6, 23, 50),
            Bucket(2026, 6, 22, 999));

        CodexLocalUsageTotals result =
            CodexDailyUsageAggregator.Aggregate(source, Now, TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 7, 22), result.LocalToday);
        Assert.Equal(800, result.TodayTokens);
        Assert.Equal(400, result.YesterdayTokens);
        Assert.Equal(1300, result.Last7DaysTokens);
        Assert.Equal(1350, result.Last30DaysTokens);
    }

    [Fact]
    public void TimeZoneControlsTodayWithoutChangingProviderCivilDates()
    {
        TimeZoneInfo plusFourteen = TimeZoneInfo.CreateCustomTimeZone(
            "Test/Plus14",
            TimeSpan.FromHours(14),
            "Test +14",
            "Test +14");
        DateTimeOffset utc = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        CodexTokenUsageSnapshot source = Snapshot(Bucket(2026, 7, 22, 7));

        CodexLocalUsageTotals result =
            CodexDailyUsageAggregator.Aggregate(source, utc, plusFourteen);

        Assert.Equal(new DateOnly(2026, 7, 22), result.LocalToday);
        Assert.Equal(7, result.TodayTokens);
    }

    [Fact]
    public void MissingDayDiffersFromAnObservedZero()
    {
        CodexLocalUsageTotals empty = CodexDailyUsageAggregator.Aggregate(
            Snapshot(),
            Now,
            TimeZoneInfo.Utc);
        CodexLocalUsageTotals zero = CodexDailyUsageAggregator.Aggregate(
            Snapshot(Bucket(2026, 7, 22, 0)),
            Now,
            TimeZoneInfo.Utc);

        Assert.Null(empty.TodayTokens);
        Assert.Null(empty.YesterdayTokens);
        Assert.Null(empty.Last7DaysTokens);
        Assert.Null(empty.Last30DaysTokens);
        Assert.Equal(0, zero.TodayTokens);
        Assert.Null(zero.YesterdayTokens);
        Assert.Equal(0, zero.Last7DaysTokens);
        Assert.Equal(0, zero.Last30DaysTokens);
    }

    [Fact]
    public void DuplicateCivilDateFailsClosedWithoutEchoingUsage()
    {
        CodexTokenUsageSnapshot source = Snapshot(
            Bucket(2026, 7, 22, 1),
            Bucket(2026, 7, 22, 2));

        CodexProtocolException error = Assert.Throws<CodexProtocolException>(() =>
            CodexDailyUsageAggregator.Aggregate(source, Now, TimeZoneInfo.Utc));

        Assert.Equal("Codex daily usage could not be mapped safely.", error.Message);
        Assert.DoesNotContain("2026", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodOverflowFailsClosed()
    {
        CodexTokenUsageSnapshot source = Snapshot(
            Bucket(2026, 7, 22, long.MaxValue),
            Bucket(2026, 7, 21, 1));

        Assert.Throws<CodexProtocolException>(() =>
            CodexDailyUsageAggregator.Aggregate(source, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ObservationTimeMustBeUtc()
    {
        Assert.Throws<ArgumentException>(() =>
            CodexDailyUsageAggregator.Aggregate(
                Snapshot(),
                Now.ToOffset(TimeSpan.FromHours(-3)),
                TimeZoneInfo.Utc));
    }

    private static CodexTokenUsageSnapshot Snapshot(params CodexUsageDailyBucket[] buckets) =>
        new(new CodexUsageSummary(null, null, null, null, null), buckets);

    private static CodexUsageDailyBucket Bucket(int year, int month, int day, long tokens) =>
        new(new DateOnly(year, month, day), tokens);
}
