using System.Runtime.CompilerServices;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Cache;

public sealed class CacheFirstRefresh
{
    private readonly SnapshotStore _store;
    private readonly IReadOnlyList<IProviderRuntime> _providers;
    private readonly TimeProvider _clock;
    private readonly ProviderOperationGate? _providerOperationGate;

    public CacheFirstRefresh(
        SnapshotStore store,
        IEnumerable<IProviderRuntime> providers,
        TimeProvider clock,
        ProviderOperationGate? providerOperationGate = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(providers);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        IProviderRuntime[] providerArray = providers.ToArray();
        if (providerArray.Any(provider => provider is null))
        {
            throw new ArgumentException("Providers cannot contain null values.", nameof(providers));
        }

        string? duplicateProvider = providerArray
            .GroupBy(provider => provider.Descriptor.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateProvider is not null)
        {
            throw new ArgumentException(
                $"Provider '{duplicateProvider}' appears more than once.",
                nameof(providers));
        }

        _providers = Array.AsReadOnly(providerArray);
        _providerOperationGate = providerOperationGate;
    }

    public async IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SnapshotCacheReadResult readResult = await _store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        yield return new CacheFirstEvent.CachePublished(readResult);

        var lastGood = readResult is SnapshotCacheReadResult.Loaded loaded
            ? loaded.Snapshots.ToDictionary(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            : new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);

        foreach (IProviderRuntime provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CacheFirstEvent.ProviderCompleted completed;
            if (_providerOperationGate is null)
            {
                completed = await RefreshProviderAsync(
                        provider,
                        lastGood,
                        forceRefresh,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await using IAsyncDisposable lease = await _providerOperationGate
                    .EnterAsync(cancellationToken)
                    .ConfigureAwait(false);
                completed = await RefreshProviderAsync(
                        provider,
                        lastGood,
                        forceRefresh,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            yield return completed;
        }
    }

    private async Task<CacheFirstEvent.ProviderCompleted> RefreshProviderAsync(
        IProviderRuntime provider,
        Dictionary<string, ProviderSnapshot> lastGood,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        lastGood.TryGetValue(provider.Descriptor.Id.Value, out ProviderSnapshot? cachedSnapshot);
        var context = new RefreshContext(_clock, cachedSnapshot, forceRefresh);
        ProviderOutcome outcome = await provider
            .RefreshAsync(context, cancellationToken)
            .ConfigureAwait(false);

        ProviderSnapshot? newLastGood = outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            _ => null,
        };

        if (newLastGood is not null
            && !string.Equals(
                newLastGood.ProviderId.Value,
                provider.Descriptor.Id.Value,
                StringComparison.Ordinal))
        {
            outcome = new ProviderOutcome.ContractFailure(
                new ProviderError(
                    ProviderErrorCode.ContractViolation,
                    "The provider returned a snapshot with a different provider ID."),
                cachedSnapshot);
            newLastGood = null;
        }

        CacheUpdateStatus cacheStatus = CacheUpdateStatus.NotAttempted;
        if (newLastGood is not null)
        {
            try
            {
                SnapshotCacheSaveResult saveResult = await _store
                    .UpsertLastGoodAsync(newLastGood, cancellationToken)
                    .ConfigureAwait(false);
                cacheStatus = saveResult switch
                {
                    SnapshotCacheSaveResult.Saved => CacheUpdateStatus.Updated,
                    SnapshotCacheSaveResult.RefusedUnsupportedVersion =>
                        CacheUpdateStatus.RefusedUnsupportedVersion,
                    _ => CacheUpdateStatus.Rejected,
                };
                if (cacheStatus == CacheUpdateStatus.Updated)
                {
                    lastGood[provider.Descriptor.Id.Value] = newLastGood;
                }
            }
            catch (IOException)
            {
                cacheStatus = CacheUpdateStatus.IoFailure;
            }
            catch (UnauthorizedAccessException)
            {
                cacheStatus = CacheUpdateStatus.AccessDenied;
            }
            catch (TimeoutException)
            {
                cacheStatus = CacheUpdateStatus.LockTimedOut;
            }
            catch (InvalidOperationException)
            {
                cacheStatus = CacheUpdateStatus.Rejected;
            }
        }

        return new CacheFirstEvent.ProviderCompleted(
            provider.Descriptor.Id,
            outcome,
            cacheStatus);
    }
}
