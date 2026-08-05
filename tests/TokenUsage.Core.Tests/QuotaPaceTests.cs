using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests;

public sealed class QuotaPaceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30, QuotaPaceStatus.Ahead, 60)]
    [InlineData(46, QuotaPaceStatus.OnTrack, 92)]
    [InlineData(50, QuotaPaceStatus.OnTrack, 100)]
    [InlineData(60, QuotaPaceStatus.Behind, 120)]
    public void HalfWindowClassifiesProjectedUsage(
        decimal used,
        QuotaPaceStatus expectedStatus,
        decimal expectedProjected)
    {
        TimeSpan week = TimeSpan.FromDays(7);

        QuotaPaceResult result = Assert.IsType<QuotaPaceResult>(
            QuotaPace.Evaluate(used, 100m, Now.Add(week / 2), week, Now));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedProjected, result.ProjectedUsage);
        Assert.Equal(expectedStatus == QuotaPaceStatus.Behind, result.TimeToExhaust is not null);
    }

    [Fact]
    public void EvaluationWaitsForOnePercentOrOneMinute()
    {
        TimeSpan session = TimeSpan.FromMinutes(300);
        TimeSpan minimum = TimeSpan.FromMinutes(3);
        DateTimeOffset reset = Now.Add(session - minimum);

        Assert.Null(QuotaPace.Evaluate(1m, 100m, reset.AddSeconds(1), session, Now));
        Assert.NotNull(QuotaPace.Evaluate(1m, 100m, reset, session, Now));

        TimeSpan shortWindow = TimeSpan.FromMinutes(60);
        DateTimeOffset shortReset = Now.Add(shortWindow - TimeSpan.FromMinutes(1));
        Assert.NotNull(QuotaPace.Evaluate(1m, 100m, shortReset, shortWindow, Now));
    }

    [Fact]
    public void MissingInvalidOrFinishedWindowHidesPace()
    {
        Assert.Null(QuotaPace.Evaluate(50m, 100m, null, TimeSpan.FromHours(1), Now));
        Assert.Null(QuotaPace.Evaluate(50m, 100m, Now.AddHours(1), null, Now));
        Assert.Null(QuotaPace.Evaluate(50m, 100m, Now, TimeSpan.FromHours(1), Now));
        Assert.Null(QuotaPace.Evaluate(50m, 100m, Now.AddHours(2), TimeSpan.FromHours(1), Now));
        Assert.Null(QuotaPace.Evaluate(50m, 0m, Now.AddHours(1), TimeSpan.FromHours(2), Now));
    }

    [Fact]
    public void ZeroAndExhaustedBoundariesStayExplicit()
    {
        TimeSpan window = TimeSpan.FromHours(2);
        DateTimeOffset reset = Now.AddHours(1);

        QuotaPaceResult zero = Assert.IsType<QuotaPaceResult>(
            QuotaPace.Evaluate(0m, 100m, reset, window, Now));
        QuotaPaceResult exhausted = Assert.IsType<QuotaPaceResult>(
            QuotaPace.Evaluate(100m, 100m, reset, window, Now));

        Assert.Equal(QuotaPaceStatus.Ahead, zero.Status);
        Assert.Equal(0m, zero.ProjectedUsage);
        Assert.Null(zero.TimeToExhaust);
        Assert.Equal(QuotaPaceStatus.Behind, exhausted.Status);
        Assert.Null(exhausted.TimeToExhaust);
    }

    [Fact]
    public void UtcInputsAreRequired()
    {
        DateTimeOffset localNow = Now.ToOffset(TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() =>
            QuotaPace.Evaluate(
                50m,
                100m,
                Now.AddHours(1),
                TimeSpan.FromHours(2),
                localNow));
    }

    [Fact]
    public void SubTickExhaustionKeepsBehindStatusWithoutEta()
    {
        TimeSpan window = TimeSpan.FromHours(2);

        QuotaPaceResult result = Assert.IsType<QuotaPaceResult>(
            QuotaPace.Evaluate(
                99.99999999999999999999999999m,
                100m,
                Now.AddHours(1),
                window,
                Now));

        Assert.Equal(QuotaPaceStatus.Behind, result.Status);
        Assert.Null(result.TimeToExhaust);
    }
}
