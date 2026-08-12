using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsagePeriodPolicyTests
{
    [Fact]
    public void RollingDisplayWindowCountsTodayAsOneOfItsThirtyDays()
    {
        DateOnly today = new(2026, 7, 22);

        DateOnly start = UsagePeriodPolicy.RollingDisplayStart(today);

        Assert.Equal(new DateOnly(2026, 6, 23), start);
        Assert.Equal(
            UsagePeriodPolicy.RollingDisplayDays,
            today.DayNumber - start.DayNumber + 1);
    }

    [Fact]
    public void ReconciliationWindowReachesFurtherBackThanTheDisplayWindow()
    {
        DateOnly today = new(2026, 7, 22);

        Assert.Equal(new DateOnly(2026, 6, 18), UsagePeriodPolicy.ReconciliationStart(today));
        Assert.True(UsagePeriodPolicy.ReconciliationStart(today)
            < UsagePeriodPolicy.RollingDisplayStart(today));
        Assert.Equal(today, UsagePeriodPolicy.ReconciliationStart(today, windowDays: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsagePeriodPolicy.ReconciliationStart(today, windowDays: 0));
    }

    [Fact]
    public void QueryStartCoversTheReconciliationWindow()
    {
        DateOnly midMonth = new(2026, 7, 22);

        Assert.Equal(
            UsagePeriodPolicy.ReconciliationStart(midMonth),
            UsagePeriodPolicy.QueryStart(midMonth));
    }

    /// <summary>
    /// The month row of a card sums the current civil month, so a query that starts after the
    /// first of the month would under-report it. This holds on the last day of every month
    /// length, including a leap February.
    /// </summary>
    [Theory]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void QueryStartAlwaysReachesTheFirstOfTheCurrentMonth(int year, int month, int day)
    {
        DateOnly today = new(year, month, day);

        DateOnly start = UsagePeriodPolicy.QueryStart(today);

        Assert.True(
            start <= new DateOnly(year, month, 1),
            $"The query starts on {start:O} and misses part of {year}-{month}.");
    }
}
