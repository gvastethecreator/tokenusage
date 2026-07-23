namespace WOpenUsage.Core.Usage;

public enum QuotaUsageLevel
{
    Healthy,
    Caution,
    Warning,
    Critical,
}

public static class QuotaUsageLevelPolicy
{
    public static QuotaUsageLevel Evaluate(decimal remainingPercent)
    {
        if (remainingPercent is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingPercent),
                remainingPercent,
                "Remaining percent must be between 0 and 100.");
        }

        if (remainingPercent > 25m)
        {
            return QuotaUsageLevel.Healthy;
        }

        if (remainingPercent > 15m)
        {
            return QuotaUsageLevel.Caution;
        }

        if (remainingPercent > 5m)
        {
            return QuotaUsageLevel.Warning;
        }

        return QuotaUsageLevel.Critical;
    }
}
