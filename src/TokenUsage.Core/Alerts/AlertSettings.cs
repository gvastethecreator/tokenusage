namespace TokenUsage.Core.Alerts;

public enum AlertKind
{
    QuotaThreshold,
    ExhaustionForecast,
    StaleData,
    CredentialFailure,
}

public sealed class AlertSettings
{
    public AlertSettings(
        bool enabled,
        int quotaThresholdPercent,
        bool quotaThresholdEnabled,
        bool exhaustionForecastEnabled,
        bool staleDataEnabled,
        bool credentialFailureEnabled)
    {
        if (quotaThresholdPercent is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quotaThresholdPercent),
                quotaThresholdPercent,
                "The quota threshold must be from 1 through 99 percent.");
        }

        Enabled = enabled;
        QuotaThresholdPercent = quotaThresholdPercent;
        QuotaThresholdEnabled = quotaThresholdEnabled;
        ExhaustionForecastEnabled = exhaustionForecastEnabled;
        StaleDataEnabled = staleDataEnabled;
        CredentialFailureEnabled = credentialFailureEnabled;
    }

    public static AlertSettings Default { get; } = new(
        enabled: false,
        quotaThresholdPercent: 20,
        quotaThresholdEnabled: true,
        exhaustionForecastEnabled: true,
        staleDataEnabled: true,
        credentialFailureEnabled: true);

    public bool Enabled { get; }

    public int QuotaThresholdPercent { get; }

    public bool QuotaThresholdEnabled { get; }

    public bool ExhaustionForecastEnabled { get; }

    public bool StaleDataEnabled { get; }

    public bool CredentialFailureEnabled { get; }

    public bool IsEnabled(AlertKind kind)
    {
        bool kindEnabled = kind switch
        {
            AlertKind.QuotaThreshold => QuotaThresholdEnabled,
            AlertKind.ExhaustionForecast => ExhaustionForecastEnabled,
            AlertKind.StaleData => StaleDataEnabled,
            AlertKind.CredentialFailure => CredentialFailureEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown alert kind."),
        };

        return Enabled && kindEnabled;
    }
}
