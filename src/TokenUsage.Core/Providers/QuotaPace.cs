namespace WOpenUsage.Core.Providers;

public enum QuotaPaceStatus
{
    Ahead,
    OnTrack,
    Behind,
}

public sealed record QuotaPaceResult
{
    public QuotaPaceResult(
        QuotaPaceStatus status,
        decimal projectedUsage,
        TimeSpan? timeToExhaust)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(projectedUsage);
        if (timeToExhaust is not null && timeToExhaust.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToExhaust),
                "Time to exhaust must be positive when present.");
        }

        Status = status;
        ProjectedUsage = projectedUsage;
        TimeToExhaust = timeToExhaust;
    }

    public QuotaPaceStatus Status { get; }

    public decimal ProjectedUsage { get; }

    public TimeSpan? TimeToExhaust { get; }
}

public static class QuotaPace
{
    public static QuotaPaceResult? Evaluate(
        decimal used,
        decimal limit,
        DateTimeOffset? resetsAtUtc,
        TimeSpan? windowDuration,
        DateTimeOffset nowUtc)
    {
        UtcTimestamp.Require(nowUtc, nameof(nowUtc));
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        if (resetsAtUtc is not null)
        {
            UtcTimestamp.Require(resetsAtUtc.Value, nameof(resetsAtUtc));
        }

        if (limit <= 0m
            || resetsAtUtc is null
            || windowDuration is null
            || windowDuration <= TimeSpan.Zero
            || nowUtc >= resetsAtUtc.Value)
        {
            return null;
        }

        try
        {
            DateTimeOffset startedAtUtc = resetsAtUtc.Value - windowDuration.Value;
            TimeSpan elapsed = nowUtc - startedAtUtc;
            long minimumTicks = Math.Max(
                TimeSpan.FromMinutes(1).Ticks,
                checked((long)Math.Ceiling(windowDuration.Value.Ticks / 100m)));
            if (elapsed.Ticks < minimumTicks)
            {
                return null;
            }

            decimal projected = checked(
                used * windowDuration.Value.Ticks / elapsed.Ticks);
            QuotaPaceStatus status = projected <= limit * 0.9m
                ? QuotaPaceStatus.Ahead
                : projected <= limit
                    ? QuotaPaceStatus.OnTrack
                    : QuotaPaceStatus.Behind;

            TimeSpan? timeToExhaust = null;
            if (status == QuotaPaceStatus.Behind && used is > 0m && used < limit)
            {
                decimal etaTicks = checked(elapsed.Ticks * (limit - used) / used);
                long remainingTicks = (resetsAtUtc.Value - nowUtc).Ticks;
                if (etaTicks is >= 1m and < long.MaxValue
                    && etaTicks < remainingTicks)
                {
                    timeToExhaust = TimeSpan.FromTicks((long)etaTicks);
                }
            }

            return new QuotaPaceResult(status, projected, timeToExhaust);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
