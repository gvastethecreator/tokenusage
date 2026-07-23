using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Cli;

public static class LocalLimitsCliAccess
{
    public static async Task<IReadOnlyList<ProviderSnapshot>> ReadAsync(
        string dataDirectory,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (forceRefresh)
        {
            throw new LimitsRefreshUnavailableException();
        }

        string cachePath = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "cache",
            "providers",
            "codex",
            SnapshotStore.DefaultFileName);
        var store = new SnapshotStore(cachePath);
        SnapshotCacheReadResult result = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return result is SnapshotCacheReadResult.Loaded loaded
            ? loaded.Snapshots
                .Where(snapshot => string.Equals(
                    snapshot.ProviderId.Value,
                    "codex",
                    StringComparison.Ordinal))
                .ToArray()
            : Array.Empty<ProviderSnapshot>();
    }
}
