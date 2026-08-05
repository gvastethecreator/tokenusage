namespace TokenUsage.Core.Alerts;

public sealed class AlertNotificationIntent
{
    public AlertNotificationIntent(AlertCandidate candidate)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    public AlertCandidate Candidate { get; }

    public AlertKind Kind => Candidate.ConditionKey.Kind;

    public string ProviderId => Candidate.ConditionKey.ProviderId.Value;
}
