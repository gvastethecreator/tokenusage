using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Runtime.Windows.Codex;

namespace WOpenUsage.Cli;

public static class LocalLimitsCliAccess
{
    public static async Task<IReadOnlyList<ProviderSnapshot>> ReadAsync(
        string dataDirectory,
        string? providerId,
        bool forceRefresh,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (providerId is not null
            && !string.Equals(providerId, "codex", StringComparison.Ordinal))
        {
            return [];
        }

        string cacheDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "cache",
            "providers",
            "codex");
        if (forceRefresh)
        {
            var factory = new CodexAppServerQuotaClientFactory(clock);
            var coordinator = new CodexRefreshCoordinator(cacheDirectory, clock, factory);
            return await SelectForceResultAsync(
                coordinator.RunAsync(forceRefresh: true, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        string cachePath = Path.Combine(cacheDirectory, SnapshotStore.DefaultFileName);
        var store = new SnapshotStore(cachePath, clock);
        SnapshotCacheReadResult result = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return result is SnapshotCacheReadResult.Loaded loaded
            ? SelectCodex(loaded.Snapshots)
            : [];
    }

    internal static async Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();

        ProviderSnapshot? cached = null;
        ProviderSnapshot? result = null;
        await foreach (CacheFirstEvent item in events
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            switch (item)
            {
                case CacheFirstEvent.CachePublished published:
                    cached = SelectCodex(published.Snapshots).SingleOrDefault();
                    result ??= cached;
                    break;

                case CacheFirstEvent.ProviderCompleted completed
                    when string.Equals(
                        completed.ProviderId.Value,
                        "codex",
                        StringComparison.Ordinal):
                    result = SelectOutcomeSnapshot(completed.Outcome) ?? cached;
                    break;
            }
        }

        return result is null ? [] : [result];
    }

    private static ProviderSnapshot[] SelectCodex(
        IEnumerable<ProviderSnapshot> snapshots) =>
        snapshots
            .Where(snapshot => string.Equals(
                snapshot.ProviderId.Value,
                "codex",
                StringComparison.Ordinal))
            .Take(1)
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
