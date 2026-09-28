using TokenUsage.Core.Providers;

namespace TokenUsage.Providers;

/// <summary>
/// Next reset of a calendar spend limit that resets at a UTC boundary: midnight for a daily
/// limit, Monday midnight for a weekly limit, and the first of the month for a monthly limit.
/// Vercel AI Gateway budgets and OpenRouter key limits both document these boundaries.
/// </summary>
internal static class UtcResetSchedule
{
    public static DateTimeOffset? Next(ProgressResetCadence? cadence, DateTimeOffset nowUtc)
    {
        DateTime today = nowUtc.UtcDateTime.Date;
        return cadence switch
        {
            ProgressResetCadence.Daily => new DateTimeOffset(today.AddDays(1), TimeSpan.Zero),
            ProgressResetCadence.Weekly => new DateTimeOffset(
                today.AddDays(DaysUntilNextMonday(today.DayOfWeek)),
                TimeSpan.Zero),
            ProgressResetCadence.Monthly => new DateTimeOffset(
                today.Year,
                today.Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero).AddMonths(1),
            _ => null,
        };
    }

    private static int DaysUntilNextMonday(DayOfWeek day)
    {
        int days = ((int)DayOfWeek.Monday - (int)day + 7) % 7;
        return days == 0 ? 7 : days;
    }
}
