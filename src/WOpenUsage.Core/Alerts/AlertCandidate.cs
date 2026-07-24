namespace WOpenUsage.Core.Alerts;

public sealed class AlertCandidate
{
    private AlertCandidate(
        AlertConditionKey conditionKey,
        DateTimeOffset evaluatedAtUtc,
        decimal? remainingPercent,
        int? thresholdPercent,
        DateTimeOffset? projectedExhaustionAtUtc)
    {
        ConditionKey = conditionKey;
        EvaluatedAtUtc = evaluatedAtUtc;
        RemainingPercent = remainingPercent;
        ThresholdPercent = thresholdPercent;
        ProjectedExhaustionAtUtc = projectedExhaustionAtUtc;
    }

    public AlertConditionKey ConditionKey { get; }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public decimal? RemainingPercent { get; }

    public int? ThresholdPercent { get; }

    public DateTimeOffset? ProjectedExhaustionAtUtc { get; }

    internal static AlertCandidate ForQuotaThreshold(
        AlertConditionKey conditionKey,
        DateTimeOffset evaluatedAtUtc,
        decimal remainingPercent,
        int thresholdPercent) =>
        new(
            conditionKey,
            evaluatedAtUtc,
            remainingPercent,
            thresholdPercent,
            projectedExhaustionAtUtc: null);

    internal static AlertCandidate ForExhaustionForecast(
        AlertConditionKey conditionKey,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset projectedExhaustionAtUtc) =>
        new(
            conditionKey,
            evaluatedAtUtc,
            remainingPercent: null,
            thresholdPercent: null,
            projectedExhaustionAtUtc);

    internal static AlertCandidate ForProviderState(
        AlertConditionKey conditionKey,
        DateTimeOffset evaluatedAtUtc) =>
        new(
            conditionKey,
            evaluatedAtUtc,
            remainingPercent: null,
            thresholdPercent: null,
            projectedExhaustionAtUtc: null);
}
