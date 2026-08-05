using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class QuotaUsageLevelPolicyTests
{
    [Theory]
    [InlineData("100", QuotaUsageLevel.Healthy)]
    [InlineData("25.01", QuotaUsageLevel.Healthy)]
    [InlineData("25", QuotaUsageLevel.Caution)]
    [InlineData("15.01", QuotaUsageLevel.Caution)]
    [InlineData("15", QuotaUsageLevel.Warning)]
    [InlineData("5.01", QuotaUsageLevel.Warning)]
    [InlineData("5", QuotaUsageLevel.Critical)]
    [InlineData("0", QuotaUsageLevel.Critical)]
    public void MapsRemainingPercentToExpectedLevel(
        string remainingPercent,
        QuotaUsageLevel expected)
    {
        Assert.Equal(
            expected,
            QuotaUsageLevelPolicy.Evaluate(decimal.Parse(
                remainingPercent,
                System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    public void RejectsPercentOutsideRange(string remainingPercent)
    {
        decimal value = decimal.Parse(
            remainingPercent,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuotaUsageLevelPolicy.Evaluate(value));
    }
}
