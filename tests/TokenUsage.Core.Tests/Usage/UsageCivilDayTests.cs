using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageCivilDayTests
{
    [Fact]
    public void UtcMinusThreePlacesOneAmUtcOnThePreviousCivilDay()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone(
            "utc-minus-three",
            TimeSpan.FromHours(-3),
            "UTC-3",
            "UTC-3");
        (DateTimeOffset from, DateTimeOffset to) = UsageCivilDay.UtcBounds(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            zone);
        var oneAmUtc = new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 3, 0, 0, TimeSpan.Zero), to);
        Assert.True(oneAmUtc < from);
        (DateTimeOffset previousFrom, DateTimeOffset previousTo) = UsageCivilDay.UtcBounds(
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 9, 12),
            zone);
        Assert.True(oneAmUtc >= previousFrom && oneAmUtc < previousTo);
    }

    [Fact]
    public void PacificCivilMidnightStaysValidAcrossASpringForwardDay()
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        DateOnly springForward = new(2026, 3, 8);
        (DateTimeOffset from, DateTimeOffset to) = UsageCivilDay.UtcBounds(
            springForward,
            springForward,
            zone);
        DateTime localMidnight = springForward.ToDateTime(TimeOnly.MinValue);
        Assert.False(zone.IsInvalidTime(localMidnight));
        Assert.Equal(
            new DateTimeOffset(localMidnight, zone.GetUtcOffset(localMidnight)).ToUniversalTime(),
            from);
        DateTime nextMidnight = springForward.AddDays(1).ToDateTime(TimeOnly.MinValue);
        Assert.Equal(
            new DateTimeOffset(nextMidnight, zone.GetUtcOffset(nextMidnight)).ToUniversalTime(),
            to);
        Assert.True(to > from);
    }
}
