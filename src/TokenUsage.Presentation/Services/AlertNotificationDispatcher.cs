using System.Globalization;
using TokenUsage.App.Localization;
using TokenUsage.Core.Alerts;

namespace TokenUsage.App.Services;

public sealed class AlertNotificationDispatcher
{
    private readonly IAlertNotificationSink _sink;
    private readonly Func<string, string> _getString;

    public AlertNotificationDispatcher(
        IAlertNotificationSink sink,
        Func<string, string> getString)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
    }

    public async Task DeliverAsync(
        IEnumerable<AlertNotificationIntent> intents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intents);
        foreach (AlertNotificationIntent intent in intents)
        {
            AlertNotificationMessage message = CreateMessage(intent);
            await _sink.ShowAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    public AlertNotificationMessage CreateMessage(AlertNotificationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        AlertCandidate candidate = intent.Candidate;
        string providerName = ProviderDisplayName.Resolve(intent.ProviderId, _getString);
        return intent.Kind switch
        {
            AlertKind.QuotaThreshold => new AlertNotificationMessage(
                string.Format(CultureInfo.CurrentCulture, _getString("AlertQuotaTitleFormat"), providerName),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _getString("AlertQuotaBodyFormat"),
                    Math.Round(candidate.RemainingPercent ?? 0m),
                    candidate.ThresholdPercent ?? 0),
                CreateQuotaTarget(candidate)),
            AlertKind.ExhaustionForecast => new AlertNotificationMessage(
                string.Format(CultureInfo.CurrentCulture, _getString("AlertExhaustionTitleFormat"), providerName),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _getString("AlertExhaustionBodyFormat"),
                    candidate.ProjectedExhaustionAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                        ?? _getString("AlertTimeUnavailable")),
                CreateQuotaTarget(candidate)),
            AlertKind.StaleData => new AlertNotificationMessage(
                string.Format(CultureInfo.CurrentCulture, _getString("AlertStaleTitleFormat"), providerName),
                _getString("AlertStaleBody"),
                new AlertActivationTarget(AlertActivationArea.ProviderStatus, intent.ProviderId)),
            AlertKind.CredentialFailure => new AlertNotificationMessage(
                string.Format(CultureInfo.CurrentCulture, _getString("AlertCredentialTitleFormat"), providerName),
                _getString("AlertCredentialBody"),
                new AlertActivationTarget(AlertActivationArea.ProviderStatus, intent.ProviderId)),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "Unknown alert kind."),
        };
    }

    private static AlertActivationTarget CreateQuotaTarget(AlertCandidate candidate) =>
        new(
            AlertActivationArea.QuotaReport,
            candidate.ConditionKey.ProviderId.Value,
            candidate.ConditionKey.MetricId!.Value);
}
