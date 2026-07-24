using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Runtime.Windows.Codex;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.Cli;

public static class LocalLimitsCliAccess
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

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
            && !IsKnownProvider(providerId))
        {
            return [];
        }

        string root = Path.GetFullPath(dataDirectory);
        ProviderRefreshHost host = CreateLiveHost(root, clock);
        if (forceRefresh)
        {
            return await SelectForceResultAsync(
                host.RunAsync(forceRefresh: true, cancellationToken),
                providerId,
                cancellationToken).ConfigureAwait(false);
        }

        var snapshots = new List<ProviderSnapshot>();
        foreach (ProviderRefreshRegistration registration in host.Registrations)
        {
            if (providerId is not null
                && !string.Equals(
                    registration.Provider.Descriptor.Id.Value,
                    providerId,
                    StringComparison.Ordinal))
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

    internal static ProviderRefreshHost CreateLiveHost(string dataDirectory, TimeProvider clock)
    {
        string codexCacheDirectory = Path.Combine(dataDirectory, "cache", "providers", "codex");
        string vercelCacheDirectory = Path.Combine(
            dataDirectory,
            "cache",
            "providers",
            "vercel-ai-gateway");
        var codexCoordinator = new CodexRefreshCoordinator(
            codexCacheDirectory,
            clock,
            new CodexAppServerQuotaClientFactory(clock));
        var vercelCoordinator = new VercelGatewayRefreshCoordinator(
            vercelCacheDirectory,
            clock,
            SharedHttpClient);
        return new ProviderRefreshHost(
            [
                codexCoordinator.CreateRegistration(),
                vercelCoordinator.CreateRegistration(),
            ],
            clock);
    }

    internal static async Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        string? providerId,
        CancellationToken cancellationToken)
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
                    foreach (ProviderSnapshot snapshot in SelectProviders(published.Snapshots, providerId))
                    {
                        cached[snapshot.ProviderId.Value] = snapshot;
                        results.TryAdd(snapshot.ProviderId.Value, snapshot);
                    }

                    break;

                case CacheFirstEvent.ProviderCompleted completed
                    when providerId is null
                         || string.Equals(
                             completed.ProviderId.Value,
                             providerId,
                             StringComparison.Ordinal):
                    ProviderSnapshot? fromOutcome = SelectOutcomeSnapshot(completed.Outcome);
                    if (fromOutcome is not null)
                    {
                        results[completed.ProviderId.Value] = fromOutcome;
                    }
                    else if (cached.TryGetValue(completed.ProviderId.Value, out ProviderSnapshot? lastGood))
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

    // Back-compat for existing tests that call the codex-only selector.
    internal static Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        CancellationToken cancellationToken) =>
        SelectForceResultAsync(events, providerId: "codex", cancellationToken);

    private static bool IsKnownProvider(string providerId) =>
        string.Equals(providerId, "codex", StringComparison.Ordinal)
        || string.Equals(providerId, "vercel-ai-gateway", StringComparison.Ordinal);

    private static ProviderSnapshot[] SelectProviders(
        IEnumerable<ProviderSnapshot> snapshots,
        string? providerId) =>
        snapshots
            .Where(snapshot => providerId is null
                || string.Equals(
                    snapshot.ProviderId.Value,
                    providerId,
                    StringComparison.Ordinal))
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
