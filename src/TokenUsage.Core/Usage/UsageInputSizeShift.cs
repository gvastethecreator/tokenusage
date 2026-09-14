namespace TokenUsage.Core.Usage;

public sealed record UsageInputSizeShift(
    UsageStatisticAvailability Availability,
    string Reason,
    long? BaselineMedianInputTokens,
    long? CurrentMedianInputTokens,
    long? AbsoluteChange,
    string? PercentChange)
{
    public const string MethodVersion = "input-size-shift/v1";
    public const int MinimumRequestsPerSide = 30;
    public const int MinimumSessionsWhenAvailable = 5;
    public const long MinimumAbsoluteChange = 1_000;
    public const decimal MinimumPercentChange = 0.25m;

    public static UsageInputSizeShift Evaluate(
        UsageDistributionEligibility baseline,
        UsageDistributionEligibility current,
        int baselineSessionCount,
        int currentSessionCount,
        bool sessionsAvailable)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegative(baselineSessionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(currentSessionCount);

        if (baseline.EligibleFinalCount < MinimumRequestsPerSide
            || current.EligibleFinalCount < MinimumRequestsPerSide)
        {
            return new UsageInputSizeShift(
                UsageStatisticAvailability.InsufficientSample,
                "Input-size shift needs 30 eligible final observations on each side.",
                null,
                null,
                null,
                null);
        }

        if (sessionsAvailable
            && (baselineSessionCount < MinimumSessionsWhenAvailable
                || currentSessionCount < MinimumSessionsWhenAvailable))
        {
            return new UsageInputSizeShift(
                UsageStatisticAvailability.InsufficientSample,
                "Input-size shift needs 5 comparable sessions on each side when sessions are available.",
                null,
                null,
                null,
                null);
        }

        if (baseline.MedianInputTokens is not { } baselineMedian
            || current.MedianInputTokens is not { } currentMedian)
        {
            return new UsageInputSizeShift(
                UsageStatisticAvailability.Unavailable,
                "Input-size shift needs measured medians on both sides.",
                null,
                null,
                null,
                null);
        }

        long absolute = currentMedian - baselineMedian;
        if (baselineMedian == 0)
        {
            return new UsageInputSizeShift(
                UsageStatisticAvailability.MeasuredZero,
                "Percentage change is omitted because the baseline median is zero.",
                baselineMedian,
                currentMedian,
                absolute,
                null);
        }

        decimal percent = (decimal)absolute / baselineMedian;
        if (Math.Abs(absolute) < MinimumAbsoluteChange
            || Math.Abs(percent) < MinimumPercentChange)
        {
            return new UsageInputSizeShift(
                UsageStatisticAvailability.Unavailable,
                "The median change is below 1,000 input tokens or 25% of a positive baseline.",
                baselineMedian,
                currentMedian,
                absolute,
                percent.ToString("0.0%", System.Globalization.CultureInfo.InvariantCulture));
        }

        return new UsageInputSizeShift(
            UsageStatisticAvailability.Measured,
            MethodVersion,
            baselineMedian,
            currentMedian,
            absolute,
            percent.ToString("0.0%", System.Globalization.CultureInfo.InvariantCulture));
    }
}
