using WOpenUsage.Core.Alerts;

namespace WOpenUsage.Core.Tests.Alerts;

public sealed class AlertSettingsTests
{
    [Fact]
    public void DefaultsAreQuietUntilTheMasterSwitchIsEnabled()
    {
        AlertSettings settings = AlertSettings.Default;

        Assert.False(settings.Enabled);
        Assert.Equal(20, settings.QuotaThresholdPercent);
        Assert.True(settings.QuotaThresholdEnabled);
        Assert.True(settings.ExhaustionForecastEnabled);
        Assert.True(settings.StaleDataEnabled);
        Assert.True(settings.CredentialFailureEnabled);
        Assert.All(Enum.GetValues<AlertKind>(), kind => Assert.False(settings.IsEnabled(kind)));
    }

    [Theory]
    [InlineData(AlertKind.QuotaThreshold, true)]
    [InlineData(AlertKind.ExhaustionForecast, false)]
    [InlineData(AlertKind.StaleData, true)]
    [InlineData(AlertKind.CredentialFailure, false)]
    public void PerKindFlagsApplyWhenMasterSwitchIsEnabled(AlertKind kind, bool expected)
    {
        var settings = new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 15,
            quotaThresholdEnabled: true,
            exhaustionForecastEnabled: false,
            staleDataEnabled: true,
            credentialFailureEnabled: false);

        Assert.Equal(expected, settings.IsEnabled(kind));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-1)]
    public void InvalidThresholdIsRejected(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertSettings(
            true,
            threshold,
            true,
            true,
            true,
            true));
    }

    [Fact]
    public void UndefinedKindIsRejectedEvenWhenMasterSwitchIsDisabled()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlertSettings.Default.IsEnabled((AlertKind)999));
    }
}
