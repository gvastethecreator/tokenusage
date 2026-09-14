using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public enum UsageStatisticAvailability
{
    Unavailable = 0,
    InsufficientSample = 1,
    MeasuredZero = 2,
    Measured = 3,
}

public sealed record UsageDistributionEligibility(
    UsageStatisticAvailability Availability,
    string Reason,
    int EligibleFinalCount,
    long? MedianInputTokens,
    long? Percentile95InputTokens)
{
    public const int MedianMinimumSamples = 5;
    public const int Percentile95MinimumSamples = 100;

    public static UsageDistributionEligibility Unavailable(string reason) =>
        new(UsageStatisticAvailability.Unavailable, reason, 0, null, null);

    public static UsageDistributionEligibility FromEvents(IReadOnlyList<UsageEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return FromMeasuredInputs(events
            .Where(row => row.DetailMetadata.RecordKind == UsageRecordKind.RequestFinal
                && row.DetailMetadata.Input == UsageComponentAvailability.Measured)
            .Select(row => row.Tokens.Input));
    }

    public static UsageDistributionEligibility FromMeasuredInputs(IEnumerable<long> measuredInputs)
    {
        ArgumentNullException.ThrowIfNull(measuredInputs);
        long[] inputs = measuredInputs.OrderBy(value => value).ToArray();
        if (inputs.Length == 0)
        {
            return Unavailable(
                "No source has proved RequestFinal observations with measured input.");
        }

        if (inputs.Length < MedianMinimumSamples)
        {
            return new UsageDistributionEligibility(
                UsageStatisticAvailability.InsufficientSample,
                "Fewer than 5 eligible final observations.",
                inputs.Length,
                null,
                null);
        }

        long median = NearestRank(inputs, 0.5);
        long? p95 = inputs.Length >= Percentile95MinimumSamples
            ? NearestRank(inputs, 0.95)
            : null;
        UsageStatisticAvailability availability = median == 0 && inputs.All(value => value == 0)
            ? UsageStatisticAvailability.MeasuredZero
            : UsageStatisticAvailability.Measured;
        return new UsageDistributionEligibility(
            availability,
            availability == UsageStatisticAvailability.MeasuredZero
                ? "Eligible final observations have measured zero input."
                : "Eligible final observations.",
            inputs.Length,
            median,
            p95);
    }

    public static long NearestRank(IReadOnlyList<long> ordered, double percentile)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (ordered.Count == 0)
        {
            throw new ArgumentException("Nearest-rank needs at least one value.", nameof(ordered));
        }

        if (percentile is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        int rank = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        if (rank < 0)
        {
            rank = 0;
        }

        if (rank >= ordered.Count)
        {
            rank = ordered.Count - 1;
        }

        return ordered[rank];
    }

    public static long Median(IReadOnlyList<long> ordered) => NearestRank(ordered, 0.5);

    public static long MedianAbsoluteDeviation(IReadOnlyList<long> values, long median)
    {
        ArgumentNullException.ThrowIfNull(values);
        long[] deviations = values
            .Select(value => Math.Abs(value - median))
            .OrderBy(value => value)
            .ToArray();
        return Median(deviations);
    }
}
