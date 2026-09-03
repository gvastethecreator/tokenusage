using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.Providers.Tests;

public sealed class UsageReportCycleComparisonTests
{
    [Fact]
    public void CompatibleCyclesExposeRawValuesAndEfficiencyDeltas()
    {
        UsageReportCycleComparison comparison = UsageReportCycleComparisonCalculator.Compare(
            Observation("quota.primary:300", quota: 50m, tokens: 2_000, cost: 4m, events: 8, pricedTokens: 1_000),
            Observation("quota.primary:300", quota: 25m, tokens: 500, cost: 1m, events: 3, pricedTokens: 500));

        Assert.True(comparison.IsCompatible);
        Assert.False(comparison.HasIncompleteCycle);
        Assert.Equal(25m, comparison.QuotaUsedPercent.Delta);
        Assert.Equal(1_500m, comparison.Tokens.Delta);
        Assert.Equal(3m, comparison.CostUsd.Delta);
        Assert.Equal(5m, comparison.EventCount.Delta);
        Assert.Equal(20m, comparison.TokensPerQuotaPoint.Delta);
        Assert.Equal(2_000m, comparison.CostPerMillionTokens.Delta);
    }

    [Fact]
    public void ZeroQuotaAndUnpricedTokensKeepEfficiencyUnavailable()
    {
        UsageReportCycleComparison comparison = UsageReportCycleComparisonCalculator.Compare(
            Observation("quota.primary:300", quota: 0m, tokens: 0, cost: 0m, events: 0, pricedTokens: 0),
            Observation("quota.primary:300", quota: 0m, tokens: 0, cost: 0m, events: 0, pricedTokens: 0));

        Assert.Equal(0m, comparison.Tokens.Delta);
        Assert.Equal(0m, comparison.CostUsd.Delta);
        Assert.Null(comparison.TokensPerQuotaPoint.Left);
        Assert.Null(comparison.TokensPerQuotaPoint.Delta);
        Assert.Null(comparison.CostPerMillionTokens.Left);
        Assert.Null(comparison.CostPerMillionTokens.Delta);
    }

    [Fact]
    public void MissingValuesStayMissingInsteadOfBecomingZero()
    {
        UsageReportCycleComparison comparison = UsageReportCycleComparisonCalculator.Compare(
            Observation("quota.primary:300", quota: 10m, tokens: 100, cost: null, events: 1, pricedTokens: null),
            Observation("quota.primary:300", quota: 10m, tokens: 100, cost: 0m, events: 1, pricedTokens: 100));

        Assert.Null(comparison.CostUsd.Left);
        Assert.Equal(0m, comparison.CostUsd.Right);
        Assert.Null(comparison.CostUsd.Delta);
        Assert.Null(comparison.CostPerMillionTokens.Delta);
    }

    [Fact]
    public void DifferentCadenceAndActiveCycleAreReportedWithoutDroppingValues()
    {
        UsageReportCycleComparison comparison = UsageReportCycleComparisonCalculator.Compare(
            Observation("quota.primary:300", quota: 40m, tokens: 200, cost: 2m, events: 2, pricedTokens: 200, isComplete: false),
            Observation("quota.secondary:10080", quota: 20m, tokens: 100, cost: 1m, events: 1, pricedTokens: 100));

        Assert.False(comparison.IsCompatible);
        Assert.True(comparison.HasIncompleteCycle);
        Assert.Equal(100m, comparison.Tokens.Delta);
        Assert.Equal(1m, comparison.CostUsd.Delta);
    }

    private static UsageReportCycleObservation Observation(
        string groupId,
        decimal? quota,
        long? tokens,
        decimal? cost,
        int? events,
        long? pricedTokens,
        bool isComplete = true) => new(
            groupId,
            isComplete,
            quota,
            tokens,
            cost,
            events,
            pricedTokens);
}
