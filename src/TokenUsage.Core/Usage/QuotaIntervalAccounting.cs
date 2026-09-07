namespace TokenUsage.Core.Usage;

public enum QuotaWindowSemantics { Unknown, Fixed, Rolling }

public sealed record QuotaIntervalEvidence(QuotaWindowSemantics Semantics, bool HasCompleteAccounting,
    bool HasKnownCapacityAndEpoch, bool HasMatchingPoolUsage);

public sealed record QuotaIntervalAccounting(decimal? ConsumedPoints, decimal? ReplenishedPoints)
{
    /// <summary>Polled levels alone cannot establish consumption. Require event-complete fixed accounting.</summary>
    public static QuotaIntervalAccounting Calculate(IReadOnlyList<decimal> usedLevels, QuotaIntervalEvidence evidence)
    {
        if (evidence.Semantics != QuotaWindowSemantics.Fixed || !evidence.HasCompleteAccounting
            || !evidence.HasKnownCapacityAndEpoch || usedLevels.Count < 2
            || usedLevels.Any(value => value < 0 || value > 100)) return new(null, null);
        decimal consumed = 0, replenished = 0;
        for (int index = 1; index < usedLevels.Count; index++)
        {
            decimal change = usedLevels[index] - usedLevels[index - 1];
            consumed += Math.Max(0, change);
            replenished += Math.Max(0, -change);
        }
        return new(consumed, replenished);
    }

    public decimal? PointsPerMillionTokens(long tokens, QuotaIntervalEvidence evidence) =>
        evidence.HasMatchingPoolUsage && tokens > 0 && ConsumedPoints is { } consumed
            ? consumed * 1_000_000m / tokens : null;

    public decimal? TokensPerPoint(long tokens, QuotaIntervalEvidence evidence) =>
        evidence.HasMatchingPoolUsage && tokens >= 0 && ConsumedPoints is > 0
            ? tokens / ConsumedPoints.Value : null;
}
