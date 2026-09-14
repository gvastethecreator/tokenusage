using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageDistributionEligibilityTests
{
    [Fact]
    public void EmptyInputsAreUnavailable()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs([]);
        Assert.Equal(UsageStatisticAvailability.Unavailable, eligibility.Availability);
        Assert.Equal(0, eligibility.EligibleFinalCount);
        Assert.Null(eligibility.MedianInputTokens);
        Assert.Null(eligibility.Percentile95InputTokens);
    }

    [Fact]
    public void FourSamplesAreInsufficient()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs([1, 2, 3, 4]);
        Assert.Equal(UsageStatisticAvailability.InsufficientSample, eligibility.Availability);
        Assert.Equal(4, eligibility.EligibleFinalCount);
        Assert.Null(eligibility.MedianInputTokens);
        Assert.Null(eligibility.Percentile95InputTokens);
    }

    [Fact]
    public void FiveSamplesPublishMedianWithoutP95()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs([1, 2, 3, 4, 5]);
        Assert.Equal(UsageStatisticAvailability.Measured, eligibility.Availability);
        Assert.Equal(5, eligibility.EligibleFinalCount);
        Assert.Equal(3, eligibility.MedianInputTokens);
        Assert.Null(eligibility.Percentile95InputTokens);
    }

    [Fact]
    public void NinetyNineSamplesStillOmitP95()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs(
            Enumerable.Range(1, 99).Select(value => (long)value));
        Assert.Equal(UsageStatisticAvailability.Measured, eligibility.Availability);
        Assert.Equal(99, eligibility.EligibleFinalCount);
        Assert.Equal(50, eligibility.MedianInputTokens);
        Assert.Null(eligibility.Percentile95InputTokens);
    }

    [Fact]
    public void OneHundredSamplesPublishMedianAndP95()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs(
            Enumerable.Range(1, 100).Select(value => (long)value));
        Assert.Equal(UsageStatisticAvailability.Measured, eligibility.Availability);
        Assert.Equal(50, eligibility.MedianInputTokens);
        Assert.Equal(95, eligibility.Percentile95InputTokens);
    }

    [Fact]
    public void MeasuredZeroIsDistinctFromUnavailable()
    {
        UsageDistributionEligibility eligibility = UsageDistributionEligibility.FromMeasuredInputs([0, 0, 0, 0, 0]);
        Assert.Equal(UsageStatisticAvailability.MeasuredZero, eligibility.Availability);
        Assert.Equal(0, eligibility.MedianInputTokens);
        Assert.Null(eligibility.Percentile95InputTokens);
    }
}
