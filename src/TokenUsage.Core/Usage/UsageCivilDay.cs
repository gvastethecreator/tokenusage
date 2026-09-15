namespace TokenUsage.Core.Usage;

public static class UsageCivilDay
{
    public static (DateTimeOffset FromInclusiveUtc, DateTimeOffset ToExclusiveUtc) UtcBounds(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        DateTime startLocal = fromInclusive.ToDateTime(TimeOnly.MinValue);
        DateTime endLocal = toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue);
        DateTimeOffset from = new(startLocal, timeZone.GetUtcOffset(startLocal));
        DateTimeOffset to = new(endLocal, timeZone.GetUtcOffset(endLocal));
        return (from.ToUniversalTime(), to.ToUniversalTime());
    }
}
