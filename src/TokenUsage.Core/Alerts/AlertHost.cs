namespace WOpenUsage.Core.Alerts;

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
        AlertSettings settings = await _settingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        AlertDecisionState decisions = await _decisionStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AlertCandidate> candidates = AlertEvaluator.Evaluate(
            settings,
            evaluatedAtUtc,
            providers);
        var intents = new List<AlertNotificationIntent>();
        bool dirty = false;
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
}
