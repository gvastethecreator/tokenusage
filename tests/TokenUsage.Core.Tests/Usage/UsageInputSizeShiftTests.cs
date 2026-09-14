using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageInputSizeShiftTests
{
    [Fact]
    public void TwentyNineRequestsAreInsufficient()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(29, 1_000),
            Side(30, 2_000),
            baselineSessionCount: 5,
            currentSessionCount: 5,
            sessionsAvailable: true);
        Assert.Equal(UsageStatisticAvailability.InsufficientSample, shift.Availability);
        Assert.Null(shift.PercentChange);
    }

    [Fact]
    public void FourSessionsAreInsufficientWhenSessionsExist()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(30, 4_000),
            Side(30, 8_000),
            baselineSessionCount: 4,
            currentSessionCount: 5,
            sessionsAvailable: true);
        Assert.Equal(UsageStatisticAvailability.InsufficientSample, shift.Availability);
    }

    [Fact]
    public void ZeroSessionsSkipTheSessionFloor()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(30, 4_000),
            Side(30, 8_000),
            baselineSessionCount: 0,
            currentSessionCount: 0,
            sessionsAvailable: false);
        Assert.Equal(UsageStatisticAvailability.Measured, shift.Availability);
        Assert.Equal(4_000, shift.AbsoluteChange);
        Assert.Equal("100.0%", shift.PercentChange);
    }

    [Fact]
    public void ExactThousandAndTwentyFivePercentPublishes()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(30, 4_000),
            Side(30, 5_000),
            baselineSessionCount: 5,
            currentSessionCount: 5,
            sessionsAvailable: true);
        Assert.Equal(UsageStatisticAvailability.Measured, shift.Availability);
        Assert.Equal(1_000, shift.AbsoluteChange);
        Assert.Equal("25.0%", shift.PercentChange);
    }

    [Fact]
    public void NineHundredNinetyNineTokensDoesNotPublish()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(30, 4_000),
            Side(30, 4_999),
            baselineSessionCount: 5,
            currentSessionCount: 5,
            sessionsAvailable: true);
        Assert.Equal(UsageStatisticAvailability.Unavailable, shift.Availability);
    }

    [Fact]
    public void ZeroBaselineOmitsPercentage()
    {
        UsageInputSizeShift shift = UsageInputSizeShift.Evaluate(
            Side(30, 0),
            Side(30, 5_000),
            baselineSessionCount: 5,
            currentSessionCount: 5,
            sessionsAvailable: true);
        Assert.Equal(UsageStatisticAvailability.MeasuredZero, shift.Availability);
        Assert.Null(shift.PercentChange);
        Assert.Equal(5_000, shift.AbsoluteChange);
    }

    private static UsageDistributionEligibility Side(int count, long medianValue) =>
        UsageDistributionEligibility.FromMeasuredInputs(Enumerable.Repeat(medianValue, count));
}

public sealed class UsageSessionOutlierTests
{
    [Fact]
    public void TwentyNineSessionsAreInsufficient()
    {
        UsageSessionOutlier outlier = UsageSessionOutlier.Evaluate(Sessions(29, 100));
        Assert.Equal(UsageStatisticAvailability.InsufficientSample, outlier.Availability);
        Assert.Null(outlier.SessionKey);
    }

    [Fact]
    public void ZeroMadDoesNotPublish()
    {
        UsageSessionOutlier outlier = UsageSessionOutlier.Evaluate(Sessions(30, 100));
        Assert.Equal(UsageStatisticAvailability.Unavailable, outlier.Availability);
        Assert.Equal(0, outlier.MedianAbsoluteDeviation);
        Assert.Null(outlier.SessionKey);
    }

    [Fact]
    public void FenceIsStrictAndKeepsOneCard()
    {
        var rows = new List<(string SessionKey, long Tokens)>();
        for (int value = 1; value <= 29; value++)
        {
            rows.Add(($"n-{value:00}", value));
        }

        rows.Add(("edge", 57));
        UsageSessionOutlier equalFence = UsageSessionOutlier.Evaluate(rows);
        Assert.Equal(UsageStatisticAvailability.Unavailable, equalFence.Availability);
        Assert.Equal(15, equalFence.MedianTokens);
        Assert.Equal(7, equalFence.MedianAbsoluteDeviation);
        Assert.Null(equalFence.SessionKey);

        rows[^1] = ("small-high", 58);
        rows.Add(("large-high", 1_000));
        UsageSessionOutlier outlier = UsageSessionOutlier.Evaluate(rows);
        Assert.Equal(UsageStatisticAvailability.Measured, outlier.Availability);
        Assert.Equal("large-high", outlier.SessionKey);
        Assert.Equal(1_000, outlier.SessionTokens);
    }

    [Fact]
    public void MixedCohortPublishesTheLargestSessionAboveTheFence()
    {
        var rows = new List<(string SessionKey, long Tokens)>();
        for (int index = 0; index < 15; index++)
        {
            rows.Add(($"lo-{index:00}", 50));
        }

        for (int index = 0; index < 15; index++)
        {
            rows.Add(($"hi-{index:00}", 150));
        }

        rows.Add(("spike", 10_000));
        UsageSessionOutlier outlier = UsageSessionOutlier.Evaluate(rows);
        Assert.Equal(UsageStatisticAvailability.Measured, outlier.Availability);
        Assert.Equal("spike", outlier.SessionKey);
        Assert.Equal(10_000, outlier.SessionTokens);
    }

    private static (string SessionKey, long Tokens)[] Sessions(int count, long tokens) =>
        Enumerable.Range(0, count).Select(index => ($"session-{index:00}", tokens)).ToArray();
}
