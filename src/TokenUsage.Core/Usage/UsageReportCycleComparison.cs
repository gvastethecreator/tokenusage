namespace TokenUsage.Core.Usage;

public sealed record UsageReportCycleObservation(
    string GroupId,
    bool IsComplete,
    decimal? QuotaUsedPercent,
    long? Tokens,
    decimal? CostUsd,
    int? EventCount,
    long? PricedTokens,
    decimal? ConsumedQuotaPoints = null,
    bool HasMatchingPoolUsage = false);

public sealed record UsageReportNumericComparison(
    decimal? Left,
    decimal? Right,
    decimal? Delta);

public sealed record UsageReportCycleComparison(
    bool IsCompatible,
    bool HasIncompleteCycle,
    UsageReportNumericComparison QuotaUsedPercent,
    UsageReportNumericComparison Tokens,
    UsageReportNumericComparison CostUsd,
    UsageReportNumericComparison EventCount,
    UsageReportNumericComparison TokensPerQuotaPoint,
    UsageReportNumericComparison CostPerMillionTokens);

public static class UsageReportCycleComparisonCalculator
{
    public static UsageReportCycleComparison Compare(
        UsageReportCycleObservation left,
        UsageReportCycleObservation right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        bool compatible = string.Equals(left.GroupId, right.GroupId, StringComparison.Ordinal);
        return new UsageReportCycleComparison(
            compatible,
            !left.IsComplete || !right.IsComplete,
            Difference(left.QuotaUsedPercent, right.QuotaUsedPercent),
            Difference(ToDecimal(left.Tokens), ToDecimal(right.Tokens)),
            Difference(left.CostUsd, right.CostUsd),
            Difference(ToDecimal(left.EventCount), ToDecimal(right.EventCount)),
            compatible ? Difference(TokensPerQuotaPoint(left), TokensPerQuotaPoint(right)) : Difference(null, null),
            Difference(CostPerMillionTokens(left), CostPerMillionTokens(right)));
    }

    private static decimal? TokensPerQuotaPoint(UsageReportCycleObservation observation) =>
        observation.IsComplete && observation.HasMatchingPoolUsage
        && observation.Tokens is long tokens
        && observation.ConsumedQuotaPoints is > 0m
            ? tokens / observation.ConsumedQuotaPoints.Value
            : null;

    private static decimal? CostPerMillionTokens(UsageReportCycleObservation observation) =>
        observation.CostUsd is decimal cost
        && observation.PricedTokens is > 0
            ? cost * 1_000_000m / observation.PricedTokens.Value
            : null;

    private static decimal? ToDecimal(long? value) => value is long number ? number : null;

    private static decimal? ToDecimal(int? value) => value is int number ? number : null;

    private static UsageReportNumericComparison Difference(decimal? left, decimal? right) => new(
        left,
        right,
        left is decimal leftValue && right is decimal rightValue
            ? rightValue - leftValue
            : null);
}
