using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Alerts;

/// <summary>
/// Builds alert facts from provider snapshots and outcomes without UI types.
/// </summary>
public static class AlertFactsBuilder
{
    public static ProviderAlertFacts FromSnapshot(
        ProviderSnapshot snapshot,
        TimeProvider clock,
        bool hasCredentialFailure = false,
        TimeSpan? staleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(clock);

        bool isStale = SnapshotFreshness.IsStale(snapshot, clock, staleAfter);
        DateTimeOffset nowUtc = clock.GetUtcNow().ToUniversalTime();
        Dictionary<string, TimeSpan> durations = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .Where(metric => metric.Id.Value.EndsWith(".window-minutes", StringComparison.Ordinal)
                && metric.Value > 0m
                && metric.Value <= (decimal)TimeSpan.MaxValue.TotalMinutes)
            .ToDictionary(
                metric => metric.Id.Value[..^".window-minutes".Length],
                metric => TimeSpan.FromMinutes((double)metric.Value),
                StringComparer.Ordinal);
        var quotas = new List<QuotaAlertFacts>();
        foreach (MetricSnapshot metric in snapshot.Metrics)
        {
            if (metric is not ProgressMetricSnapshot progress)
            {
                continue;
            }

            durations.TryGetValue(progress.Id.Value, out TimeSpan windowDuration);
            QuotaPaceResult? pace = QuotaPace.Evaluate(
                progress.Used,
                progress.Limit,
                progress.ResetsAtUtc,
                windowDuration == default ? null : windowDuration,
                nowUtc);
            DateTimeOffset? projectedExhaustion = pace?.TimeToExhaust is TimeSpan eta
                ? nowUtc + eta
                : null;
            quotas.Add(new QuotaAlertFacts(
                progress.Id,
                progress.RemainingPercent,
                progress.ResetsAtUtc,
                projectedExhaustion));
        }

        return new ProviderAlertFacts(
            snapshot.ProviderId,
            isStale,
            hasCredentialFailure,
            quotas);
    }

    public static ProviderAlertFacts FromOutcome(
        ProviderId providerId,
        ProviderOutcome outcome,
        TimeProvider clock,
        TimeSpan? staleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(clock);

        bool credentialFailure = outcome is ProviderOutcome.NotConfigured
            or ProviderOutcome.UnsupportedAccount;

        ProviderSnapshot? snapshot = outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            ProviderOutcome.Throttled throttled => throttled.LastGood,
            ProviderOutcome.TransientFailure transient => transient.LastGood,
            ProviderOutcome.ContractFailure contract => contract.LastGood,
            _ => null,
        };

        if (snapshot is null)
        {
            return new ProviderAlertFacts(
                providerId,
                isStale: false,
                hasCredentialFailure: credentialFailure,
                quotas: []);
        }

        return FromSnapshot(snapshot, clock, credentialFailure, staleAfter);
    }

    public static IReadOnlyList<ProviderAlertFacts> FromCacheFirstEvents(
        IEnumerable<CacheFirstEventLike> events,
        TimeProvider clock,
        TimeSpan? staleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);

        var byProvider = new Dictionary<string, ProviderAlertFacts>(StringComparer.Ordinal);
        foreach (CacheFirstEventLike item in events)
        {
            byProvider[item.ProviderId.Value] = FromOutcome(
                item.ProviderId,
                item.Outcome,
                clock,
                staleAfter);
        }

        return byProvider.Values
            .OrderBy(facts => facts.ProviderId.Value, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
/// Lightweight host-facing view of a completed provider refresh (avoids Cache dependency in facts builder signature tests).
/// </summary>
public sealed record CacheFirstEventLike(
    ProviderId ProviderId,
    ProviderOutcome Outcome);
