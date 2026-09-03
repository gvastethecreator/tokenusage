namespace TokenUsage.Core.Alerts;

/// <summary>
/// Evaluates settings against provider facts, applies decision-key dedupe, and emits intents.
/// </summary>
public sealed class AlertHost
{
    private readonly AlertDecisionStore _decisionStore;
    private readonly AlertSettingsStore _settingsStore;

    public AlertHost(AlertDecisionStore decisionStore, AlertSettingsStore settingsStore)
    {
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<IReadOnlyList<AlertNotificationIntent>> EvaluateAsync(
        DateTimeOffset evaluatedAtUtc,
        IEnumerable<ProviderAlertFacts> providers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ProviderAlertFacts[] providerFacts = providers.ToArray();
        AlertSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AlertCandidate> candidates = AlertEvaluator.Evaluate(
            settings,
            evaluatedAtUtc,
            providerFacts);
        bool canRecoverProviderState = providerFacts.Any(provider =>
            (!provider.IsStale && settings.IsEnabled(AlertKind.StaleData))
            || (!provider.HasCredentialFailure
                && settings.IsEnabled(AlertKind.CredentialFailure)));
        if (candidates.Count == 0 && !canRecoverProviderState)
        {
            // Nothing crossed a threshold, which is the usual outcome of a refresh. The record
            // of what was already announced is only needed to decide about a candidate.
            return [];
        }

        AlertDecisionState decisions = await _decisionStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        var intents = new List<AlertNotificationIntent>();
        bool dirty = ClearRecoveredProviderStates(decisions, settings, providerFacts);
        foreach (AlertCandidate candidate in candidates)
        {
            if (decisions.HasNotified(candidate.ConditionKey))
            {
                continue;
            }

            intents.Add(new AlertNotificationIntent(candidate));
            decisions.MarkNotified(candidate.ConditionKey);
            dirty = true;
        }

        if (dirty)
        {
            await _decisionStore.SaveAsync(decisions, cancellationToken).ConfigureAwait(false);
        }

        return intents.AsReadOnly();
    }

    private static bool ClearRecoveredProviderStates(
        AlertDecisionState decisions,
        AlertSettings settings,
        IEnumerable<ProviderAlertFacts> providers)
    {
        bool dirty = false;
        foreach (ProviderAlertFacts provider in providers)
        {
            if (!provider.IsStale && settings.IsEnabled(AlertKind.StaleData))
            {
                dirty |= decisions.ClearNotified(new AlertConditionKey(
                    provider.ProviderId,
                    metricId: null,
                    AlertKind.StaleData,
                    quotaWindowResetsAtUtc: null));
            }

            if (!provider.HasCredentialFailure && settings.IsEnabled(AlertKind.CredentialFailure))
            {
                dirty |= decisions.ClearNotified(new AlertConditionKey(
                    provider.ProviderId,
                    metricId: null,
                    AlertKind.CredentialFailure,
                    quotaWindowResetsAtUtc: null));
            }
        }

        return dirty;
    }
}
