using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Alerts;

public sealed class AlertNotificationIntent
{
    public AlertNotificationIntent(AlertCandidate candidate, ProviderAccountInfo? account = null)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Account = account;
    }

    public ProviderAccountInfo? Account { get; }

    public AlertCandidate Candidate { get; }

    public AlertKind Kind => Candidate.ConditionKey.Kind;

    public string ProviderId => Candidate.ConditionKey.ProviderId.Value;
}
