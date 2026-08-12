namespace TokenUsage.Core.Usage;

/// <summary>
/// The date windows local usage runs on. The product shows a rolling 30-day window, a
/// windowed source re-reads a longer tail so a late write still lands, and a query keeps
/// the whole current month so the month row is never cut. Each window is defined once here
/// because a mismatch between them shows up as a total that does not add up.
/// </summary>
public static class UsagePeriodPolicy
{
    /// <summary>
    /// Days in the rolling window the dashboard, the report, and the tray all label as
    /// "last 30 days". The window includes today.
    /// </summary>
    public const int RollingDisplayDays = 30;

    /// <summary>
    /// Days a windowed source re-reads on every refresh. Wider than the display window so a
    /// tool that writes a session late still lands inside a reconciled range.
    /// </summary>
    public const int ReconciliationDays = 35;

    /// <summary>
    /// First day of the rolling display window that ends on <paramref name="today"/>.
    /// </summary>
    public static DateOnly RollingDisplayStart(DateOnly today) =>
        today.AddDays(-(RollingDisplayDays - 1));

    /// <summary>
    /// First day a windowed source reconciles for a refresh that ends on
    /// <paramref name="today"/>.
    /// </summary>
    public static DateOnly ReconciliationStart(DateOnly today) =>
        ReconciliationStart(today, ReconciliationDays);

    /// <summary>
    /// First day a source with its own window reconciles.
    /// </summary>
    public static DateOnly ReconciliationStart(DateOnly today, int windowDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowDays, 1);
        return today.AddDays(-(windowDays - 1));
    }

    /// <summary>
    /// First day a rollup query must cover. The reconciliation window is wider than the
    /// longest month, so reading from here always includes the whole current month and the
    /// month row on a card is never cut short.
    /// </summary>
    public static DateOnly QueryStart(DateOnly today) => ReconciliationStart(today);
}
