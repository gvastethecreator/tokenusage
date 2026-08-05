using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Automation;

public sealed class LimitsQuery
{
    private readonly ProviderRefreshHost _host;

    public LimitsQuery(ProviderRefreshHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<IReadOnlyList<ProviderSnapshot>> ReadAsync(
        ProviderId? providerId,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (providerId is not null && !_host.Registrations.Any(registration =>
                registration.Provider.Descriptor.Id == providerId))
        {
            return [];
        }

        if (forceRefresh)
        {
            IAsyncEnumerable<CacheFirstEvent> events = providerId is null
                ? _host.RunAsync(forceRefresh: true, cancellationToken)
                : _host.RunProviderAsync(providerId, forceRefresh: true, cancellationToken);
            return await SelectForceResultAsync(events, providerId, cancellationToken)
                .ConfigureAwait(false);
        }

        var snapshots = new List<ProviderSnapshot>();
        foreach (ProviderRefreshRegistration registration in _host.Registrations)
        {
            if (providerId is not null
                && registration.Provider.Descriptor.Id != providerId)
            {
                continue;
            }

            SnapshotCacheReadResult result = await registration.Store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result is SnapshotCacheReadResult.Loaded loaded)
            {
                snapshots.AddRange(SelectProviders(loaded.Snapshots, providerId));
            }
        }

        return snapshots;
    }

    public static async Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        ProviderId? providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();

        var cached = new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);
        var results = new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);
        await foreach (CacheFirstEvent item in events
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            switch (item)
            {
                case CacheFirstEvent.CachePublished published:
                    foreach (ProviderSnapshot snapshot in SelectProviders(
                                 published.Snapshots,
                                 providerId))
                    {
                        cached[snapshot.ProviderId.Value] = snapshot;
                        results.TryAdd(snapshot.ProviderId.Value, snapshot);
                    }

                    break;

                case CacheFirstEvent.ProviderCompleted completed when providerId is null
                    || completed.ProviderId == providerId:
                    ProviderSnapshot? fromOutcome = SelectOutcomeSnapshot(completed.Outcome);
                    if (fromOutcome is not null)
                    {
                        results[completed.ProviderId.Value] = fromOutcome;
                    }
                    else if (cached.TryGetValue(
                                 completed.ProviderId.Value,
                                 out ProviderSnapshot? lastGood))
                    {
                        results[completed.ProviderId.Value] = lastGood;
                    }

                    break;
            }
        }

        return results.Values
            .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProviderSnapshot[] SelectProviders(
        IEnumerable<ProviderSnapshot> snapshots,
        ProviderId? providerId) =>
        snapshots
            .Where(snapshot => providerId is null || snapshot.ProviderId == providerId)
            .ToArray();

    private static ProviderSnapshot? SelectOutcomeSnapshot(ProviderOutcome outcome) => outcome switch
    {
        ProviderOutcome.Success success => success.Snapshot,
        ProviderOutcome.PartialSuccess partial => partial.Snapshot,
        ProviderOutcome.Throttled throttled => throttled.LastGood,
        ProviderOutcome.TransientFailure transient => transient.LastGood,
        ProviderOutcome.ContractFailure contract => contract.LastGood,
        _ => null,
    };
}
