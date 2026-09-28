using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

internal static class CoverageAggregation
{
    public static CoverageKind Worst(CoverageKind left, CoverageKind right) =>
        Rank(left) >= Rank(right) ? left : right;

    private static int Rank(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => 0,
        CoverageKind.Partial => 1,
        CoverageKind.SummaryOnly => 2,
        CoverageKind.Unpriced => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };
}
