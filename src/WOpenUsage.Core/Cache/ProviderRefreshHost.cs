using System.Runtime.CompilerServices;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Cache;

/// <summary>
/// Registers provider runtimes against (optionally partitioned) snapshot stores and
/// streams a single cache-first refresh pass for App, CLI, and other hosts.
/// </summary>
public sealed class ProviderRefreshRegistration
{
    public ProviderRefreshRegistration(
        IProviderRuntime provider,
        SnapshotStore store,
        ProviderOperationGate? operationGate = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OperationGate = operationGate;
    }

    public IProviderRuntime Provider { get; }

    public SnapshotStore Store { get; }

    public ProviderOperationGate? OperationGate { get; }
}

public sealed class ProviderRefreshHost
{
    private readonly IReadOnlyList<ProviderRefreshRegistration> _registrations;
    private readonly TimeProvider _clock;

    public ProviderRefreshHost(
        IEnumerable<ProviderRefreshRegistration> registrations,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        ProviderRefreshRegistration[] array = registrations.ToArray();
        if (array.Length == 0 || array.Any(registration => registration is null))
        {
            throw new ArgumentException(
                "At least one provider registration is required.",
                nameof(registrations));
        }

        string? duplicate = array
            .GroupBy(registration => registration.Provider.Descriptor.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Provider '{duplicate}' appears more than once.",
                nameof(registrations));
        }

        _registrations = Array.AsReadOnly(array);
    }

    public TimeProvider Clock => _clock;

    public IReadOnlyList<ProviderRefreshRegistration> Registrations => _registrations;

    public async IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var mergedSnapshots = new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);
        var loadedAny = false;
        SnapshotCacheReadResult? firstEmptyOrCorrupt = null;

        foreach (ProviderRefreshRegistration registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotCacheReadResult readResult = await registration.Store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (readResult is SnapshotCacheReadResult.Loaded loaded)
            {
                loadedAny = true;
                foreach (ProviderSnapshot snapshot in loaded.Snapshots)
                {
                    mergedSnapshots[snapshot.ProviderId.Value] = snapshot;
                }
            }
            else
            {
                firstEmptyOrCorrupt ??= readResult;
            }
        }

        SnapshotCacheReadResult published = loadedAny
            ? new SnapshotCacheReadResult.Loaded(
                mergedSnapshots.Values
                    .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
                    .ToArray())
            : firstEmptyOrCorrupt ?? new SnapshotCacheReadResult.Empty();
        yield return new CacheFirstEvent.CachePublished(published);

        foreach (ProviderRefreshRegistration registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CacheFirstRefresh partitionRefresh = new(
                registration.Store,
                [registration.Provider],
                _clock,
                registration.OperationGate);

            await foreach (CacheFirstEvent item in partitionRefresh
                               .RunAsync(forceRefresh, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (item is CacheFirstEvent.ProviderCompleted completed)
                {
                    yield return completed;
                }
            }
        }
    }
}
